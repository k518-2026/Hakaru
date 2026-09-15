using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hakaru.Models;

namespace Hakaru.Services;

/// <summary>
/// ドライブの空き容量いっぱいまで検証可能なパターンを書き込み、読み戻して照合します。
/// 「64GB」などと偽って実際は小容量のフラッシュメモリ（容量偽装品）は、
/// 途中でアドレスが巻き戻るため、読み戻しでヘッダーのオフセットが食い違います。
/// H2testw / F3 と同じ考え方です。
/// </summary>
public sealed class CapacityTestService
{
    public const int Block = 1 << 20;             // 1 MiB
    public const long FilePartBytes = 1L << 30;   // 1 GiB / ファイル
    public const long SafetyMargin = 32L << 20;   // 末尾に残す空き
    public const string FolderName = "__hakaru_captest__";

    private const int ERROR_DISK_FULL = 112;
    private const int ERROR_HANDLE_DISK_FULL = 39;

    public Task<CapacityTestResult> RunAsync(string rootPath, long? limitBytes, bool keepFiles,
                                             IProgress<CapacityProgress>? progress, CancellationToken ct)
        => Task.Run(() => Run(rootPath, limitBytes, keepFiles, progress, ct), ct);

    private CapacityTestResult Run(string rootPath, long? limitBytes, bool keepFiles,
                                   IProgress<CapacityProgress>? progress, CancellationToken ct)
    {
        // try の外で止める。中で止めると Finish がテストフォルダーの削除を試みてしまう
        DriveService.EnsureNotSystemVolume(rootPath);

        var result = new CapacityTestResult();
        string folder = Path.Combine(rootPath, FolderName);
        ulong seed = unchecked((ulong)Random.Shared.NextInt64()) | 1UL;

        try
        {
            Directory.CreateDirectory(folder);
            progress?.Report(new CapacityProgress(CapacityPhase.Preparing, 0, 0, 0, TimeSpan.Zero));

            long free = new DriveInfo(rootPath).AvailableFreeSpace;
            long target = free - SafetyMargin;
            if (limitBytes is long lim) target = Math.Min(target, lim);
            target = Math.Max(0, target / Block * Block);
            result.TargetBytes = target;

            if (target < Block)
            {
                result.Verdict = CapacityVerdict.Error;
                result.ErrorMessage = "NoFreeSpace";
                return result;
            }

            int parts;
            using (var buf = new AlignedBuffer(Block))
            {
                parts = WritePhase(folder, target, seed, buf, result, progress, ct);
                if (result.Canceled) return Finish(result, folder, keepFiles, progress);

                VerifyPhase(folder, parts, seed, buf, result, progress, ct);
            }

            if (result.Canceled) return Finish(result, folder, keepFiles, progress);

            result.Verdict = result.FirstBadByte >= 0 ? CapacityVerdict.Suspicious : CapacityVerdict.Genuine;
            return Finish(result, folder, keepFiles, progress);
        }
        catch (OperationCanceledException)
        {
            result.Canceled = true;
            return Finish(result, folder, keepFiles, progress);
        }
        catch (Exception ex)
        {
            result.Verdict = CapacityVerdict.Error;
            result.ErrorMessage = ex.Message;
            return Finish(result, folder, keepFiles, progress);
        }
    }

    private static int WritePhase(string folder, long target, ulong seed, AlignedBuffer buf,
                                  CapacityTestResult r, IProgress<CapacityProgress>? progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        long written = 0, lastTick = 0;
        int part = 0;
        bool diskFull = false;

        while (written < target && !diskFull)
        {
            ct.ThrowIfCancellationRequested();
            long partBytes = Math.Min(FilePartBytes, target - written) / Block * Block;
            if (partBytes == 0) break;

            string fp = PartPath(folder, part);
            using (var f = UnbufferedFile.Create(fp))
            {
                long fdone = 0;
                while (fdone < partBytes)
                {
                    ct.ThrowIfCancellationRequested();
                    Pattern.Fill(buf, Block, written, seed);

                    int n;
                    try { n = f.Write(buf, Block); }
                    catch (Win32Exception w) when (w.NativeErrorCode is ERROR_DISK_FULL or ERROR_HANDLE_DISK_FULL)
                    { diskFull = true; break; }

                    if (n < Block) { diskFull = true; break; }

                    fdone += Block;
                    written += Block;
                    r.BytesWritten = written;   // 中止時の「ここまでで書き込み」表示に使う

                    if (sw.ElapsedMilliseconds - lastTick >= 150)
                    {
                        lastTick = sw.ElapsedMilliseconds;
                        double bps = written / sw.Elapsed.TotalSeconds;
                        var eta = bps > 0 ? TimeSpan.FromSeconds((target - written) / bps) : TimeSpan.Zero;
                        progress?.Report(new CapacityProgress(CapacityPhase.Writing, written, target, bps, eta));
                    }
                }
                try { f.Flush(); } catch { }
            }
            part++;
        }

        sw.Stop();
        r.BytesWritten = written;
        r.WriteBps = written / Math.Max(0.001, sw.Elapsed.TotalSeconds);
        progress?.Report(new CapacityProgress(CapacityPhase.Writing, written, target, r.WriteBps, TimeSpan.Zero));
        return part;   // 書き込んだファイル数
    }

    private static void VerifyPhase(string folder, int parts, ulong seed, AlignedBuffer buf,
                                    CapacityTestResult r, IProgress<CapacityProgress>? progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        long verified = 0, lastTick = 0;
        long total = r.BytesWritten;

        for (int part = 0; part < parts; part++)
        {
            string fp = PartPath(folder, part);
            if (!File.Exists(fp)) break;

            long flen = new FileInfo(fp).Length / Block * Block;
            using var f = UnbufferedFile.OpenRead(fp, sequential: true);

            long fpos = 0;
            while (fpos < flen)
            {
                ct.ThrowIfCancellationRequested();
                int n = f.Read(buf, Block);
                if (n <= 0) break;
                n = n / Pattern.Page * Pattern.Page;

                long badRel = Pattern.Verify(buf, n, verified, seed, out var kind, out long decoded);
                if (badRel >= 0)
                {
                    r.FirstBadByte = verified + badRel;
                    r.FirstBadKind = kind;
                    r.AliasedToOffset = decoded;
                    r.GoodBytes = verified + badRel;
                    sw.Stop();
                    r.ReadBps = verified / Math.Max(0.001, sw.Elapsed.TotalSeconds);
                    return;
                }

                verified += n;
                fpos += n;
                r.GoodBytes = verified;

                if (sw.ElapsedMilliseconds - lastTick >= 150)
                {
                    lastTick = sw.ElapsedMilliseconds;
                    double bps = verified / sw.Elapsed.TotalSeconds;
                    var eta = bps > 0 ? TimeSpan.FromSeconds((total - verified) / bps) : TimeSpan.Zero;
                    progress?.Report(new CapacityProgress(CapacityPhase.Verifying, verified, total, bps, eta));
                }
            }
        }

        sw.Stop();
        r.GoodBytes = verified;
        r.ReadBps = verified / Math.Max(0.001, sw.Elapsed.TotalSeconds);
        progress?.Report(new CapacityProgress(CapacityPhase.Verifying, verified, total, r.ReadBps, TimeSpan.Zero));
    }

    private static CapacityTestResult Finish(CapacityTestResult r, string folder, bool keepFiles,
                                             IProgress<CapacityProgress>? progress)
    {
        if (!keepFiles)
        {
            progress?.Report(new CapacityProgress(CapacityPhase.CleaningUp, 0, 0, 0, TimeSpan.Zero));
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); } catch { }
        }
        progress?.Report(new CapacityProgress(CapacityPhase.Done, r.GoodBytes, r.TargetBytes, 0, TimeSpan.Zero));
        return r;
    }

    private static string PartPath(string folder, int index)
        => Path.Combine(folder, $"hakaru_{index:D5}.bin");
}

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hakaru.Models;

namespace Hakaru.Services;

/// <summary>
/// 逐次 / ランダムの読み書き速度を、OS キャッシュを介さずに測ります。
/// CrystalDiskMark の SEQ1M / RND4K(Q1T1) に近い測り方です。
/// </summary>
public sealed class BenchmarkService
{
    public const int SeqBlock = 1 << 20;    // 1 MiB
    public const int RandBlock = 4 << 10;   // 4 KiB
    public const long MinFileBytes = 64L << 20;

    private static readonly TimeSpan RandDuration = TimeSpan.FromSeconds(4);

    // ドライブ直下にファイルは置かずフォルダーを作る。C:\ 直下は一般ユーザーにフォルダー作成しか許されていない
    public const string FolderName = "__hakaru_bench__";
    private const string TempName = "bench.tmp";

    public Task<BenchmarkResult> RunAsync(string rootPath, long fileBytes,
                                          IProgress<BenchProgress>? progress, CancellationToken ct)
        => Task.Run(() => Run(rootPath, fileBytes, progress, ct), ct);

    private BenchmarkResult Run(string rootPath, long fileBytes, IProgress<BenchProgress>? progress, CancellationToken ct)
    {
        fileBytes = Math.Max(MinFileBytes, fileBytes / SeqBlock * SeqBlock);
        string folder = Path.Combine(rootPath, FolderName);
        string path = Path.Combine(folder, TempName);
        var result = new BenchmarkResult { TestFileBytes = fileBytes };

        using var buf = new AlignedBuffer(SeqBlock);
        Pattern.Fill(buf, SeqBlock, 0, 0xB0);

        try
        {
            Directory.CreateDirectory(folder);
            SeqWrite(path, buf, fileBytes, result, progress, ct);
            SeqRead(path, buf, fileBytes, result, progress, ct);
            RandomPass(path, fileBytes, isWrite: true, result, progress, ct);
            RandomPass(path, fileBytes, isWrite: false, result, progress, ct);
        }
        catch (OperationCanceledException)
        {
            result.Canceled = true;
        }
        finally
        {
            TryDelete(folder);
        }

        return result;
    }

    private static void SeqWrite(string path, AlignedBuffer buf, long fileBytes, BenchmarkResult r,
                                 IProgress<BenchProgress>? progress, CancellationToken ct)
    {
        using var f = UnbufferedFile.Create(path);
        var sw = Stopwatch.StartNew();
        long lastTick = 0, done = 0;

        while (done < fileBytes)
        {
            ct.ThrowIfCancellationRequested();
            f.Write(buf, SeqBlock);
            done += SeqBlock;

            if (sw.ElapsedMilliseconds - lastTick >= 100)
            {
                lastTick = sw.ElapsedMilliseconds;
                progress?.Report(new BenchProgress(BenchPhase.SeqWrite, (double)done / fileBytes,
                    done / sw.Elapsed.TotalSeconds));
            }
        }
        f.Flush();
        sw.Stop();
        r.SeqWriteBps = done / sw.Elapsed.TotalSeconds;
        progress?.Report(new BenchProgress(BenchPhase.SeqWrite, 1, r.SeqWriteBps));
    }

    private static void SeqRead(string path, AlignedBuffer buf, long fileBytes, BenchmarkResult r,
                                IProgress<BenchProgress>? progress, CancellationToken ct)
    {
        using var f = UnbufferedFile.OpenRead(path, sequential: true);
        var sw = Stopwatch.StartNew();
        long lastTick = 0, done = 0;

        while (done < fileBytes)
        {
            ct.ThrowIfCancellationRequested();
            int n = f.Read(buf, SeqBlock);
            if (n <= 0) break;
            done += n;

            if (sw.ElapsedMilliseconds - lastTick >= 100)
            {
                lastTick = sw.ElapsedMilliseconds;
                progress?.Report(new BenchProgress(BenchPhase.SeqRead, (double)done / fileBytes,
                    done / sw.Elapsed.TotalSeconds));
            }
        }
        sw.Stop();
        r.SeqReadBps = done / sw.Elapsed.TotalSeconds;
        progress?.Report(new BenchProgress(BenchPhase.SeqRead, 1, r.SeqReadBps));
    }

    private static void RandomPass(string path, long fileBytes, bool isWrite, BenchmarkResult r,
                                   IProgress<BenchProgress>? progress, CancellationToken ct)
    {
        long blocks = fileBytes / RandBlock;
        var rng = new Random(12345);
        var phase = isWrite ? BenchPhase.RandWrite : BenchPhase.RandRead;

        using var rbuf = new AlignedBuffer(RandBlock);
        if (isWrite) Pattern.Fill(rbuf, RandBlock, 0, 0xB0);

        using UnbufferedFile f = isWrite
            ? UnbufferedFile.OpenReadWrite(path)
            : UnbufferedFile.OpenRead(path, sequential: false);

        var sw = Stopwatch.StartNew();
        long ops = 0, lastTick = 0;

        while (sw.Elapsed < RandDuration)
        {
            ct.ThrowIfCancellationRequested();
            long off = rng.NextInt64(blocks) * RandBlock;
            f.Seek(off);
            if (isWrite) f.Write(rbuf, RandBlock);
            else f.Read(rbuf, RandBlock);
            ops++;

            if (sw.ElapsedMilliseconds - lastTick >= 100)
            {
                lastTick = sw.ElapsedMilliseconds;
                double bps = ops * (double)RandBlock / sw.Elapsed.TotalSeconds;
                progress?.Report(new BenchProgress(phase, sw.Elapsed.TotalMilliseconds / RandDuration.TotalMilliseconds, bps));
            }
        }
        if (isWrite) f.Flush();
        sw.Stop();

        double seconds = sw.Elapsed.TotalSeconds;
        double finalBps = ops * (double)RandBlock / seconds;
        double iops = ops / seconds;

        if (isWrite) { r.RandWriteBps = finalBps; r.RandWriteIops = iops; }
        else { r.RandReadBps = finalBps; r.RandReadIops = iops; }

        progress?.Report(new BenchProgress(phase, 1, finalBps));
    }

    private static void TryDelete(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); } catch { }
    }
}

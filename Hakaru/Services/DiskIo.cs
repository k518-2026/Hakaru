using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Hakaru.Models;
using static Hakaru.Services.NativeMethods;

namespace Hakaru.Services;

/// <summary>バッファなし I/O 用のセクター境界に整列したネイティブバッファ。</summary>
internal sealed unsafe class AlignedBuffer : IDisposable
{
    public const int Alignment = 4096;   // 一般的なセクターサイズ（512 / 4096）の公倍数

    private byte* _ptr;
    public int Size { get; }

    public AlignedBuffer(int size)
    {
        Size = size;
        _ptr = (byte*)NativeMemory.AlignedAlloc((nuint)size, Alignment);
    }

    public byte* Ptr => _ptr;
    public IntPtr Handle => (IntPtr)_ptr;

    public void Dispose()
    {
        if (_ptr != null) { NativeMemory.AlignedFree(_ptr); _ptr = null; }
    }
}

/// <summary>
/// <c>FILE_FLAG_NO_BUFFERING</c> で開いたファイル。OS のキャッシュを介さないため、
/// USB メモリなどの本当の読み書き速度を測れます。オフセット / サイズはすべて
/// <see cref="AlignedBuffer.Alignment"/> の倍数である必要があります。
/// </summary>
internal sealed class UnbufferedFile : IDisposable
{
    private readonly SafeFileHandle _h;
    private UnbufferedFile(SafeFileHandle h) => _h = h;

    public static UnbufferedFile Create(string path)
    {
        var h = CreateFileW(path, GENERIC_READ | GENERIC_WRITE,
            FILE_SHARE_READ, IntPtr.Zero, CREATE_ALWAYS,
            FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH, IntPtr.Zero);
        if (h.IsInvalid) throw Fail(path);
        return new UnbufferedFile(h);
    }

    public static UnbufferedFile OpenRead(string path, bool sequential)
    {
        uint flags = FILE_FLAG_NO_BUFFERING | (sequential ? FILE_FLAG_SEQUENTIAL_SCAN : FILE_FLAG_RANDOM_ACCESS);
        var h = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, flags, IntPtr.Zero);
        if (h.IsInvalid) throw Fail(path);
        return new UnbufferedFile(h);
    }

    public static UnbufferedFile OpenReadWrite(string path)
    {
        var h = CreateFileW(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ,
            IntPtr.Zero, OPEN_EXISTING,
            FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH | FILE_FLAG_RANDOM_ACCESS, IntPtr.Zero);
        if (h.IsInvalid) throw Fail(path);
        return new UnbufferedFile(h);
    }

    public void Seek(long offset)
    {
        if (!SetFilePointerEx(_h, offset, out _, FILE_BEGIN))
            throw Fail("seek");
    }

    public int Read(AlignedBuffer buf, int count)
    {
        if (!ReadFile(_h, buf.Handle, (uint)count, out uint read, IntPtr.Zero))
        {
            int err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err);
        }
        return (int)read;
    }

    public int Write(AlignedBuffer buf, int count)
    {
        if (!WriteFile(_h, buf.Handle, (uint)count, out uint written, IntPtr.Zero))
        {
            int err = Marshal.GetLastWin32Error();
            throw new Win32Exception(err);
        }
        return (int)written;
    }

    public void Flush() => FlushFileBuffers(_h);

    public void Dispose() => _h.Dispose();

    private static Exception Fail(string what)
        => new IOException($"{what}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
}

/// <summary>
/// 検証可能な決定的パターン。各 4 KiB ページの先頭に「テスト空間内の絶対オフセット」と
/// 「シード」を埋め込み、残りは xorshift 乱数で埋めます。
/// 読み戻したときにヘッダーのオフセットが食い違えば「容量偽装（アドレスの巻き戻り）」、
/// 乱数列が食い違えば「データ破損」と判定できます。
/// </summary>
internal static unsafe class Pattern
{
    public const int Page = 4096;
    private const ulong Golden = 0x9E3779B97F4A7C15UL;

    public static void Fill(AlignedBuffer buf, int count, long absOffset, ulong seed)
    {
        byte* b = buf.Ptr;
        for (int p = 0; p < count; p += Page)
            FillPage(b + p, absOffset + p, seed);
    }

    private static void FillPage(byte* page, long absOffset, ulong seed)
    {
        *(long*)page = absOffset;
        *(long*)(page + 8) = (long)seed;
        ulong s = seed ^ (ulong)absOffset ^ Golden;
        if (s == 0) s = 1;
        for (int i = 16; i < Page; i += 8)
        {
            s ^= s << 13; s ^= s >> 7; s ^= s << 17;
            *(long*)(page + i) = (long)s;
        }
    }

    /// <summary>
    /// バッファ内容を検証します。戻り値は最初に食い違ったページのバッファ内オフセット
    /// （問題なければ -1）。<paramref name="kind"/> に破損の種類、
    /// <paramref name="decodedOffset"/> にヘッダーから読み取れた絶対オフセットを返します。
    /// </summary>
    public static long Verify(AlignedBuffer buf, int count, long absOffset, ulong seed,
                              out VerifyKind kind, out long decodedOffset)
    {
        kind = VerifyKind.Ok;
        decodedOffset = -1;
        byte* b = buf.Ptr;

        for (int p = 0; p < count; p += Page)
        {
            byte* page = b + p;
            long ao = absOffset + p;
            long hdrOffset = *(long*)page;
            ulong hdrSeed = (ulong)*(long*)(page + 8);

            if (hdrOffset != ao || hdrSeed != seed)
            {
                decodedOffset = hdrOffset;
                bool looksAliased = hdrSeed == seed && hdrOffset >= 0 && (hdrOffset % Page) == 0;
                kind = looksAliased ? VerifyKind.Aliased : VerifyKind.Corrupt;
                return p;
            }

            ulong s = seed ^ (ulong)ao ^ Golden;
            if (s == 0) s = 1;
            for (int i = 16; i < Page; i += 8)
            {
                s ^= s << 13; s ^= s >> 7; s ^= s << 17;
                if (*(long*)(page + i) != (long)s)
                {
                    kind = VerifyKind.Corrupt;
                    return p;
                }
            }
        }
        return -1;
    }
}

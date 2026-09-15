using System;
using Hakaru.Localization;

namespace Hakaru.Common;

/// <summary>表示用の書式ユーティリティ。数値は選択中の言語の文化圏で整形します。</summary>
public static class Fmt
{
    private static readonly string[] Bin = { "B", "KiB", "MiB", "GiB", "TiB", "PiB" };
    private static readonly string[] Dec = { "B", "KB", "MB", "GB", "TB", "PB" };

    /// <summary>1024 基準（KiB/MiB/GiB…）。</summary>
    public static string Bytes(long value)
    {
        double v = Math.Abs(value);
        int u = 0;
        while (v >= 1024 && u < Bin.Length - 1) { v /= 1024; u++; }
        string num = v.ToString(v >= 100 || u == 0 ? "0" : "0.0", LocalizationManager.Culture);
        return (value < 0 ? "-" : "") + $"{num} {Bin[u]}";
    }

    /// <summary>1000 基準（KB/MB/GB…）。ドライブの「表記容量」に合わせるとき用。</summary>
    public static string BytesDecimal(long value)
    {
        double v = Math.Abs(value);
        int u = 0;
        while (v >= 1000 && u < Dec.Length - 1) { v /= 1000; u++; }
        string num = v.ToString(v >= 100 || u == 0 ? "0" : "0.00", LocalizationManager.Culture);
        return (value < 0 ? "-" : "") + $"{num} {Dec[u]}";
    }

    /// <summary>転送速度（MB/s、1,000,000 バイト基準。CrystalDiskMark と同じ）。</summary>
    public static string Speed(double bytesPerSecond)
    {
        double mb = bytesPerSecond / 1_000_000.0;
        if (mb >= 1000) return (mb / 1000).ToString("0.00", LocalizationManager.Culture) + " GB/s";
        return mb.ToString(mb >= 100 ? "0" : "0.0", LocalizationManager.Culture) + " MB/s";
    }

    public static string Iops(double iops)
    {
        if (iops >= 100_000) return (iops / 1000).ToString("0", LocalizationManager.Culture) + "k IOPS";
        return iops.ToString("0", LocalizationManager.Culture) + " IOPS";
    }

    public static string Duration(TimeSpan t)
    {
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
        if (t.TotalMinutes >= 1) return $"{t.Minutes}:{t.Seconds:00}";
        return t.TotalSeconds.ToString("0.0", LocalizationManager.Culture) + " s";
    }

    public static string Percent(double ratio)
    {
        double p = Math.Clamp(ratio, 0, 1) * 100;
        return p.ToString(p >= 10 ? "0" : "0.0", LocalizationManager.Culture) + "%";
    }
}

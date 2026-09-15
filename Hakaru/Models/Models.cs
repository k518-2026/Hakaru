using System;

namespace Hakaru.Models;

public sealed record DriveItem(
    string RootPath,
    string Display,
    bool IsRemovable,
    bool IsSystem,
    long TotalSize,
    long FreeSpace)
{
    public override string ToString() => Display;
}

// ---- ベンチマーク ----

public enum BenchPhase { SeqWrite, SeqRead, RandWrite, RandRead }

public readonly record struct BenchProgress(BenchPhase Phase, double Ratio, double CurrentBps);

public sealed class BenchmarkResult
{
    public double SeqReadBps { get; set; }
    public double SeqWriteBps { get; set; }
    public double RandReadBps { get; set; }
    public double RandWriteBps { get; set; }
    public double RandReadIops { get; set; }
    public double RandWriteIops { get; set; }
    public long TestFileBytes { get; set; }
    public bool Canceled { get; set; }
}

// ---- 容量テスト ----

/// <summary>読み戻し検証の結果種別。</summary>
public enum VerifyKind { Ok, Corrupt, Aliased }

public enum CapacityVerdict { NotRun, Genuine, Suspicious, Error }

public enum CapacityPhase { Preparing, Writing, Verifying, CleaningUp, Done }

public readonly record struct CapacityProgress(
    CapacityPhase Phase, long BytesDone, long BytesTotal, double CurrentBps, TimeSpan Eta);

public sealed class CapacityTestResult
{
    public CapacityVerdict Verdict { get; set; } = CapacityVerdict.NotRun;

    /// <summary>テスト対象にした容量（書き込みを試みた総量）。</summary>
    public long TargetBytes { get; set; }

    /// <summary>実際に書き込めた総量（末尾で容量不足になるとここで止まる）。</summary>
    public long BytesWritten { get; set; }

    /// <summary>読み戻して内容が一致した先頭からの総量 ＝ 実効容量の目安。</summary>
    public long GoodBytes { get; set; }

    /// <summary>最初に異常が出た位置（バイト）。異常なしなら -1。</summary>
    public long FirstBadByte { get; set; } = -1;

    public VerifyKind FirstBadKind { get; set; }

    /// <summary>破損部で読めた「別の」オフセット（アドレス巻き戻りの証拠）。</summary>
    public long AliasedToOffset { get; set; } = -1;

    public double WriteBps { get; set; }
    public double ReadBps { get; set; }
    public bool Canceled { get; set; }
    public string? ErrorMessage { get; set; }
}

using System;
using System.Collections.Generic;
using System.IO;
using Hakaru.Common;
using Hakaru.Models;

namespace Hakaru.Services;

/// <summary>OS のボリュームに対してテストや削除をしようとしたときに投げる。</summary>
public sealed class SystemDriveException : InvalidOperationException
{
    public string TargetPath { get; }
    public SystemDriveException(string path) : base($"Refused: {path} is on the Windows volume.") => TargetPath = path;
}

public static class DriveService
{
    public static IReadOnlyList<DriveItem> GetDrives()
    {
        var list = new List<DriveItem>();

        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch { return list; }

        foreach (var d in drives)
        {
            bool ready;
            try { ready = d.IsReady; } catch { ready = false; }
            if (!ready) continue;

            if (d.DriveType is DriveType.CDRom or DriveType.Network or DriveType.NoRootDirectory or DriveType.Unknown)
                continue;

            // OS のドライブは一覧に出さない（選べなければ誤って書き込むこともない）
            if (IsSystemVolume(d.RootDirectory.FullName))
                continue;

            long total, free;
            string label;
            try
            {
                total = d.TotalSize;
                free = d.TotalFreeSpace;
                label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "" : d.VolumeLabel;
            }
            catch { continue; }

            bool removable = d.DriveType == DriveType.Removable;

            string kind = removable ? "USB" : "HDD/SSD";
            string display = string.IsNullOrEmpty(label)
                ? $"{d.Name}  [{kind}]  {Fmt.BytesDecimal(total)}"
                : $"{d.Name}  {label}  [{kind}]  {Fmt.BytesDecimal(total)}";

            list.Add(new DriveItem(d.RootDirectory.FullName, display, removable, total, free));
        }

        return list;
    }

    /// <summary>
    /// パスが Windows の入っているボリューム上にあれば true。
    /// ドライブ文字だけでなくボリューム GUID でも比べるので、subst やマウントで同じボリュームを
    /// 別の文字から指していても見逃さない。判定できないものは安全側に倒して true を返す。
    /// </summary>
    public static bool IsSystemVolume(string path)
    {
        try
        {
            string? sysRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            string? root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(sysRoot) || string.IsNullOrEmpty(root)) return true;
            if (string.Equals(root, sysRoot, StringComparison.OrdinalIgnoreCase)) return true;

            string? sysVol = VolumeGuid(sysRoot);
            string? vol = VolumeGuid(root);
            if (sysVol is null || vol is null) return true;
            return string.Equals(sysVol, vol, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>テストを始める直前の最終確認。OS のボリュームなら何も書き込まずに例外を投げる。</summary>
    public static void EnsureNotSystemVolume(string path)
    {
        if (IsSystemVolume(path))
            throw new SystemDriveException(path);
    }

    private static string? VolumeGuid(string root)
    {
        if (!root.EndsWith('\\')) root += "\\";
        var buf = new char[64];
        return NativeMethods.GetVolumeNameForVolumeMountPointW(root, buf, (uint)buf.Length)
            ? new string(buf).TrimEnd('\0')
            : null;
    }

    /// <summary>ルートパス（例 "E:\\"）のセクターサイズ。取得できなければ 4096。</summary>
    public static uint GetSectorSize(string rootPath)
    {
        try
        {
            if (NativeMethods.GetDiskFreeSpaceW(rootPath, out _, out uint bytesPerSector, out _, out _)
                && bytesPerSector > 0)
                return bytesPerSector;
        }
        catch { }
        return 4096;
    }
}

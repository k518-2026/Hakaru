using System;
using System.Collections.Generic;
using System.IO;
using Hakaru.Common;
using Hakaru.Models;

namespace Hakaru.Services;

public static class DriveService
{
    public static IReadOnlyList<DriveItem> GetDrives()
    {
        var list = new List<DriveItem>();
        string sysRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

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
            bool system = string.Equals(d.RootDirectory.FullName, sysRoot, StringComparison.OrdinalIgnoreCase);

            string kind = removable ? "USB" : (system ? "OS" : "HDD/SSD");
            string display = string.IsNullOrEmpty(label)
                ? $"{d.Name}  [{kind}]  {Fmt.BytesDecimal(total)}"
                : $"{d.Name}  {label}  [{kind}]  {Fmt.BytesDecimal(total)}";

            list.Add(new DriveItem(d.RootDirectory.FullName, display, removable, system, total, free));
        }

        return list;
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

using System;
using System.Diagnostics;
using System.IO;

namespace Hakaru.Services;

public static class ShellService
{
    public static void OpenFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch { }
    }

    /// <summary>この端末のドライブに残った Hakaru の一時ファイル / フォルダーを削除します。</summary>
    public static bool CleanupLeftovers(string rootPath)
    {
        bool any = false;
        if (DriveService.IsSystemVolume(rootPath)) return false;   // OS のドライブでは何も消さない
        try
        {
            string folder = Path.Combine(rootPath, CapacityTestService.FolderName);
            if (Directory.Exists(folder)) { Directory.Delete(folder, true); any = true; }

            string bench = Path.Combine(rootPath, BenchmarkService.FolderName);
            if (Directory.Exists(bench)) { Directory.Delete(bench, true); any = true; }
        }
        catch { }
        return any;
    }

    public static bool HasLeftovers(string rootPath)
    {
        try
        {
            return Directory.Exists(Path.Combine(rootPath, CapacityTestService.FolderName))
                || Directory.Exists(Path.Combine(rootPath, BenchmarkService.FolderName));
        }
        catch { return false; }
    }
}

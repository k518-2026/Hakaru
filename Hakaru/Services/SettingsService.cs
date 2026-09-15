using System;
using System.IO;
using System.Text.Json;

namespace Hakaru.Services;

public sealed class AppSettings
{
    public string? Language { get; set; }
    public long BenchFileBytes { get; set; } = 1L << 30;   // 1 GiB
    public bool CapacityQuick { get; set; } = false;
    public long CapacityQuickBytes { get; set; } = 2L << 30; // 2 GiB
    public bool KeepTestFiles { get; set; } = false;
}

public static class SettingsService
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hakaru");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public static void Save(AppSettings s)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

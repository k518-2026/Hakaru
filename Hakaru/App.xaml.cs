using System.Windows;
using Hakaru.Localization;
using Hakaru.Services;

namespace Hakaru;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Settings = SettingsService.Load();
        LocalizationManager.Initialize(Settings.Language);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SettingsService.Save(Settings);
        base.OnExit(e);
    }
}

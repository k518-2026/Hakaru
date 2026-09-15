using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Hakaru.Common;
using Hakaru.Localization;
using Hakaru.Models;
using Hakaru.Services;

namespace Hakaru.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly BenchmarkService _bench = new();
    private readonly CapacityTestService _capacity = new();
    private CancellationTokenSource? _benchCts;
    private CancellationTokenSource? _capCts;

    private BenchmarkResult? _lastBench;
    private CapacityTestResult? _lastCapacity;

    public MainViewModel()
    {
        Languages = new ObservableCollection<LanguageOption>(LocalizationManager.Languages);
        _selectedLanguage = LocalizationManager.Current;

        BenchSizes = new ObservableCollection<SizeOption>(new[]
        {
            new SizeOption(256L << 20, "256 MiB"),
            new SizeOption(512L << 20, "512 MiB"),
            new SizeOption(1L << 30, "1 GiB"),
            new SizeOption(2L << 30, "2 GiB"),
            new SizeOption(4L << 30, "4 GiB"),
        });
        CapQuickSizes = new ObservableCollection<SizeOption>(new[]
        {
            new SizeOption(1L << 30, "1 GiB"),
            new SizeOption(2L << 30, "2 GiB"),
            new SizeOption(4L << 30, "4 GiB"),
            new SizeOption(8L << 30, "8 GiB"),
            new SizeOption(16L << 30, "16 GiB"),
        });

        _selectedBenchSize = BenchSizes.FirstOrDefault(s => s.Bytes == App.Settings.BenchFileBytes) ?? BenchSizes[2];
        _selectedCapQuickSize = CapQuickSizes.FirstOrDefault(s => s.Bytes == App.Settings.CapacityQuickBytes) ?? CapQuickSizes[1];
        _capModeQuick = App.Settings.CapacityQuick;
        _keepTestFiles = App.Settings.KeepTestFiles;

        RefreshDrivesCommand = new RelayCommand(_ => LoadDrives());
        RunBenchmarkCommand = new AsyncRelayCommand(_ => RunBenchmarkAsync(), _ => CanStartBench());
        StopBenchmarkCommand = new RelayCommand(_ => _benchCts?.Cancel(), _ => IsBenchRunning);
        RunCapacityCommand = new AsyncRelayCommand(_ => RunCapacityAsync(), _ => CanStartCapacity());
        StopCapacityCommand = new RelayCommand(_ => _capCts?.Cancel(), _ => IsCapRunning);
        CleanupCommand = new RelayCommand(_ => Cleanup());
        OpenDriveCommand = new RelayCommand(_ => { if (SelectedDrive is { } d) ShellService.OpenFolder(d.RootPath); });

        LocalizationManager.LanguageChanged += OnLanguageChanged;

        LoadDrives();
        BenchStatusText = L("BenchIdle");
    }

    // ---------------- Language ----------------

    public ObservableCollection<LanguageOption> Languages { get; }

    private LanguageOption _selectedLanguage;
    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (!Set(ref _selectedLanguage, value) || value is null) return;
            LocalizationManager.SetLanguage(value.Code);
            App.Settings.Language = value.Code;
        }
    }

    // ---------------- Drives ----------------

    public ObservableCollection<DriveItem> Drives { get; } = new();

    private DriveItem? _selectedDrive;
    public DriveItem? SelectedDrive
    {
        get => _selectedDrive;
        set
        {
            if (!Set(ref _selectedDrive, value)) return;
            OnPropertyChanged(nameof(DriveInfoText));
            LeftoverVisible = value is not null && ShellService.HasLeftovers(value.RootPath);
        }
    }

    public string DriveInfoText => SelectedDrive is { } d
        ? LocalizationManager.Format("DriveInfoFmt", Fmt.BytesDecimal(d.TotalSize), Fmt.Bytes(d.FreeSpace))
        : "";

    private bool _leftoverVisible;
    public bool LeftoverVisible { get => _leftoverVisible; private set => Set(ref _leftoverVisible, value); }

    public RelayCommand RefreshDrivesCommand { get; }
    public RelayCommand CleanupCommand { get; }
    public RelayCommand OpenDriveCommand { get; }

    private void LoadDrives()
    {
        var prev = SelectedDrive?.RootPath;
        Drives.Clear();
        foreach (var d in DriveService.GetDrives()) Drives.Add(d);
        SelectedDrive = Drives.FirstOrDefault(d => d.RootPath == prev)
                        ?? Drives.FirstOrDefault(d => d.IsRemovable)
                        ?? Drives.FirstOrDefault();
    }

    private void Cleanup()
    {
        if (SelectedDrive is not { } d) return;
        ShellService.CleanupLeftovers(d.RootPath);
        LeftoverVisible = ShellService.HasLeftovers(d.RootPath);
        BenchStatusText = L("Cleaned");
    }

    // ---------------- Benchmark ----------------

    public ObservableCollection<SizeOption> BenchSizes { get; }

    private SizeOption _selectedBenchSize;
    public SizeOption SelectedBenchSize
    {
        get => _selectedBenchSize;
        set { if (Set(ref _selectedBenchSize, value) && value is not null) App.Settings.BenchFileBytes = value.Bytes; }
    }

    public AsyncRelayCommand RunBenchmarkCommand { get; }
    public RelayCommand StopBenchmarkCommand { get; }

    private bool _isBenchRunning;
    public bool IsBenchRunning
    {
        get => _isBenchRunning;
        private set { if (Set(ref _isBenchRunning, value)) { OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(NotBusy)); } }
    }

    private string _benchStatusText = "";
    public string BenchStatusText { get => _benchStatusText; private set => Set(ref _benchStatusText, value); }

    private double _benchProgress;
    public double BenchProgress { get => _benchProgress; private set => Set(ref _benchProgress, value); }

    // 結果（Bps）
    private double _seqRead, _seqWrite, _randRead, _randWrite, _randReadIops, _randWriteIops;

    public string SeqReadText => _seqRead > 0 ? Fmt.Speed(_seqRead) : "—";
    public string SeqWriteText => _seqWrite > 0 ? Fmt.Speed(_seqWrite) : "—";
    public string RandReadText => _randRead > 0 ? $"{Fmt.Speed(_randRead)}   ·   {Fmt.Iops(_randReadIops)}" : "—";
    public string RandWriteText => _randWrite > 0 ? $"{Fmt.Speed(_randWrite)}   ·   {Fmt.Iops(_randWriteIops)}" : "—";

    private double BenchMax => Math.Max(1, new[] { _seqRead, _seqWrite, _randRead, _randWrite }.Max());
    public double SeqReadRatio => _seqRead / BenchMax;
    public double SeqWriteRatio => _seqWrite / BenchMax;
    public double RandReadRatio => _randRead / BenchMax;
    public double RandWriteRatio => _randWrite / BenchMax;

    private bool CanStartBench() => !IsBusy && SelectedDrive is not null;

    private async Task RunBenchmarkAsync()
    {
        if (SelectedDrive is not { } drive) return;

        _benchCts?.Dispose();
        _benchCts = new CancellationTokenSource();
        IsBenchRunning = true;
        BenchProgress = 0;
        ResetBenchResults();

        var progress = new Progress<BenchProgress>(p =>
        {
            BenchProgress = p.Ratio;
            BenchStatusText = LocalizationManager.Format("BenchRunningFmt", PhaseName(p.Phase), Fmt.Speed(p.CurrentBps));
        });

        try
        {
            var result = await _bench.RunAsync(drive.RootPath, SelectedBenchSize.Bytes, progress, _benchCts.Token);
            _lastBench = result;
            _seqRead = result.SeqReadBps;
            _seqWrite = result.SeqWriteBps;
            _randRead = result.RandReadBps;
            _randWrite = result.RandWriteBps;
            _randReadIops = result.RandReadIops;
            _randWriteIops = result.RandWriteIops;
            RaiseBenchResults();

            BenchStatusText = result.Canceled
                ? L("BenchCanceled")
                : LocalizationManager.Format("BenchDoneFmt", Fmt.Bytes(result.TestFileBytes));
        }
        catch (Exception ex)
        {
            BenchStatusText = ex.Message;
        }
        finally
        {
            IsBenchRunning = false;
            BenchProgress = 0;
            if (SelectedDrive is { } d) LeftoverVisible = ShellService.HasLeftovers(d.RootPath);
        }
    }

    private void ResetBenchResults()
    {
        _seqRead = _seqWrite = _randRead = _randWrite = _randReadIops = _randWriteIops = 0;
        RaiseBenchResults();
    }

    private void RaiseBenchResults()
    {
        foreach (var n in new[] { nameof(SeqReadText), nameof(SeqWriteText), nameof(RandReadText), nameof(RandWriteText),
                                  nameof(SeqReadRatio), nameof(SeqWriteRatio), nameof(RandReadRatio), nameof(RandWriteRatio) })
            OnPropertyChanged(n);
    }

    private static string PhaseName(BenchPhase p) => p switch
    {
        BenchPhase.SeqWrite => LocalizationManager.Get("BenchSeqWrite"),
        BenchPhase.SeqRead => LocalizationManager.Get("BenchSeqRead"),
        BenchPhase.RandWrite => LocalizationManager.Get("BenchRandWrite"),
        _ => LocalizationManager.Get("BenchRandRead"),
    };

    // ---------------- Capacity test ----------------

    public ObservableCollection<SizeOption> CapQuickSizes { get; }

    private bool _capModeQuick;
    public bool CapModeQuick
    {
        get => _capModeQuick;
        set { if (Set(ref _capModeQuick, value)) App.Settings.CapacityQuick = value; }
    }

    private SizeOption _selectedCapQuickSize;
    public SizeOption SelectedCapQuickSize
    {
        get => _selectedCapQuickSize;
        set { if (Set(ref _selectedCapQuickSize, value) && value is not null) App.Settings.CapacityQuickBytes = value.Bytes; }
    }

    private bool _keepTestFiles;
    public bool KeepTestFiles
    {
        get => _keepTestFiles;
        set { if (Set(ref _keepTestFiles, value)) App.Settings.KeepTestFiles = value; }
    }

    public AsyncRelayCommand RunCapacityCommand { get; }
    public RelayCommand StopCapacityCommand { get; }

    private bool _isCapRunning;
    public bool IsCapRunning
    {
        get => _isCapRunning;
        private set { if (Set(ref _isCapRunning, value)) { OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(NotBusy)); } }
    }

    private string _capStatusText = "";
    public string CapStatusText { get => _capStatusText; private set => Set(ref _capStatusText, value); }

    private double _capProgress;
    public double CapProgress { get => _capProgress; private set => Set(ref _capProgress, value); }

    private string _capResultTitle = "";
    public string CapResultTitle { get => _capResultTitle; private set => Set(ref _capResultTitle, value); }

    private string _capResultText = "";
    public string CapResultText
    {
        get => _capResultText;
        private set { if (Set(ref _capResultText, value)) OnPropertyChanged(nameof(HasCapResult)); }
    }

    public bool HasCapResult => !string.IsNullOrEmpty(_capResultText);

    private bool _capResultIsWarning;
    public bool CapResultIsWarning { get => _capResultIsWarning; private set => Set(ref _capResultIsWarning, value); }

    private bool CanStartCapacity() => !IsBusy && SelectedDrive is not null;

    private async Task RunCapacityAsync()
    {
        if (SelectedDrive is not { } drive) return;

        if (drive.IsSystem)
        {
            var proceed = MessageBox.Show(
                LocalizationManager.Format("SystemDriveWarnFmt", drive.RootPath),
                L("ConfirmTitle"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (proceed != MessageBoxResult.OK) return;
        }

        var ok = MessageBox.Show(L("CapWarn"), L("CapWarnTitle"),
            MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (ok != MessageBoxResult.OK) return;

        _capCts?.Dispose();
        _capCts = new CancellationTokenSource();
        IsCapRunning = true;
        CapProgress = 0;
        CapResultTitle = CapResultText = "";
        CapResultIsWarning = false;

        long? limit = CapModeQuick ? SelectedCapQuickSize.Bytes : null;

        var progress = new Progress<CapacityProgress>(p =>
        {
            CapProgress = p.BytesTotal > 0 ? (double)p.BytesDone / p.BytesTotal : 0;
            CapStatusText = p.Phase switch
            {
                CapacityPhase.Preparing => L("CapPhasePreparing"),
                CapacityPhase.CleaningUp => L("CapPhaseCleaning"),
                CapacityPhase.Done => "",
                _ => LocalizationManager.Format("CapProgressFmt",
                        p.Phase == CapacityPhase.Writing ? L("CapPhaseWriting") : L("CapPhaseVerifying"),
                        Fmt.Bytes(p.BytesDone), Fmt.Bytes(p.BytesTotal),
                        Fmt.Speed(p.CurrentBps), Fmt.Duration(p.Eta)),
            };
        });

        try
        {
            var result = await _capacity.RunAsync(drive.RootPath, limit, KeepTestFiles, progress, _capCts.Token);
            _lastCapacity = result;
            RenderCapacityResult(result);
        }
        catch (Exception ex)
        {
            CapResultTitle = L("ResultTitle");
            CapResultText = ex.Message;
        }
        finally
        {
            IsCapRunning = false;
            CapProgress = 0;
            CapStatusText = "";
            LeftoverVisible = ShellService.HasLeftovers(drive.RootPath);
        }
    }

    private void RenderCapacityResult(CapacityTestResult r)
    {
        switch (r.Verdict)
        {
            case CapacityVerdict.Genuine:
                CapResultIsWarning = false;
                CapResultTitle = L("ResultTitle");
                CapResultText = LocalizationManager.Format("CapResultGenuineFmt",
                    Fmt.Bytes(r.GoodBytes), Fmt.Speed(r.WriteBps), Fmt.Speed(r.ReadBps));
                break;

            case CapacityVerdict.Suspicious:
                CapResultIsWarning = true;
                CapResultTitle = L("CapResultSuspiciousTitle");
                string reason = r.FirstBadKind == VerifyKind.Aliased ? L("CapReasonAliased") : L("CapReasonCorrupt");
                CapResultText = LocalizationManager.Format("CapResultSuspiciousFmt",
                    Fmt.Bytes(r.GoodBytes), Fmt.Bytes(r.FirstBadByte)) + "\n" + reason;
                break;

            case CapacityVerdict.Error:
                CapResultIsWarning = true;
                CapResultTitle = L("ResultTitle");
                CapResultText = LocalizationManager.Format("CapResultErrorFmt",
                    r.ErrorMessage == "NoFreeSpace" ? L("CapNoFreeSpace") : r.ErrorMessage ?? "");
                break;

            default: // NotRun / canceled
                CapResultIsWarning = false;
                CapResultTitle = L("ResultTitle");
                CapResultText = LocalizationManager.Format("CapResultCanceledFmt",
                    Fmt.Bytes(r.BytesWritten), Fmt.Bytes(r.GoodBytes));
                break;
        }
    }

    // ---------------- shared ----------------

    public bool IsBusy => IsBenchRunning || IsCapRunning;
    public bool NotBusy => !IsBusy;

    private static string L(string key) => LocalizationManager.Get(key);

    private void OnLanguageChanged()
    {
        _selectedLanguage = LocalizationManager.Current;
        OnPropertyChanged(nameof(SelectedLanguage));
        OnPropertyChanged(nameof(DriveInfoText));
        RaiseBenchResults();

        if (!IsBenchRunning)
            BenchStatusText = _lastBench is null ? L("BenchIdle")
                : _lastBench.Canceled ? L("BenchCanceled")
                : LocalizationManager.Format("BenchDoneFmt", Fmt.Bytes(_lastBench.TestFileBytes));

        if (!IsCapRunning && _lastCapacity is not null)
            RenderCapacityResult(_lastCapacity);
    }
}

public sealed record SizeOption(long Bytes, string Label)
{
    public override string ToString() => Label;
}

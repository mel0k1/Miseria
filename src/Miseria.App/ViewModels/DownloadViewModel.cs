using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miseria.App.Services;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public sealed record BranchOption(BranchInfo Info)
{
    public string Name => Info.Name;

    public string Display
    {
        get
        {
            var date = Info.Updated == default ? string.Empty : $" · {Info.Updated:dd.MM.yyyy}";
            var password = Info.PasswordRequired ? " · требуется пароль" : string.Empty;
            return $"{Info.Name} · сборка {Info.BuildId}{date}{password}";
        }
    }
}

public partial class DownloadViewModel : ObservableObject
{
    private readonly SteamSession _session;
    private readonly InstallStore _installs;
    private SteamDownloader? _downloader;
    private CancellationTokenSource? _cancellation;
    private uint _appId;

    [ObservableProperty]
    private bool _hasContent;

    [ObservableProperty]
    private bool _isPicking;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isPreparing;

    [ObservableProperty]
    private bool _isLoadingBranches;

    [ObservableProperty]
    private string _appName = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _speedText = string.Empty;

    [ObservableProperty]
    private string _destinationText = string.Empty;

    [ObservableProperty]
    private string _destinationPreview = string.Empty;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private BranchOption? _selectedBranch;

    [ObservableProperty]
    private string _branchPassword = string.Empty;

    public ObservableCollection<BranchOption> Branches { get; } = [];

    public bool NeedsPassword => SelectedBranch?.Info.PasswordRequired == true;

    public bool HasDestination => DestinationText.Length > 0;

    public event Action? Started;

    public DownloadViewModel(SteamSession session, InstallStore installs)
    {
        _session = session;
        _installs = installs;
    }

    partial void OnDestinationTextChanged(string value) => OnPropertyChanged(nameof(HasDestination));

    partial void OnSelectedBranchChanged(BranchOption? value) => OnPropertyChanged(nameof(NeedsPassword));

    [RelayCommand]
    private void Start(AppItem app)
    {
        if (app == null)
            return;

        _appId = app.AppId;
        HasContent = true;
        IsPicking = true;
        IsActive = false;
        IsPreparing = false;
        AppName = app.Name;
        StatusText = string.Empty;
        ProgressText = string.Empty;
        SpeedText = string.Empty;
        DestinationText = string.Empty;
        ProgressPercent = 0;
        Branches.Clear();
        SelectedBranch = null;
        BranchPassword = string.Empty;
        DestinationPreview = BuildDestination(app.AppId);

        Started?.Invoke();
        _ = LoadBranchesAsync(app.AppId);
    }

    [RelayCommand]
    private void HidePicker()
    {
        IsPicking = false;
        HasContent = false;
        _cancellation?.Cancel();
    }

    [RelayCommand]
    private void BeginDownload()
    {
        if (SelectedBranch == null)
        {
            StatusText = "Выберите ветку";
            return;
        }

        if (SelectedBranch.Info.PasswordRequired && string.IsNullOrWhiteSpace(BranchPassword))
        {
            StatusText = "Введите пароль ветки";
            return;
        }

        var branch = SelectedBranch.Info.Name;
        var password = SelectedBranch.Info.PasswordRequired ? BranchPassword : null;

        IsPicking = false;
        IsActive = true;
        IsPreparing = true;
        StatusText = "Получение манифестов";
        ProgressText = string.Empty;
        SpeedText = string.Empty;
        DestinationText = string.Empty;
        ProgressPercent = 0;

        var destination = BuildDestination(_appId);

        _downloader?.Dispose();
        _downloader = _session.CreateDownloader();
        _downloader.Started += start => Dispatcher.UIThread.Post(() => OnStarted(start));
        _downloader.Progress += progress => Dispatcher.UIThread.Post(() => OnProgress(progress));
        _downloader.Finished += finish => Dispatcher.UIThread.Post(() => OnFinished(finish));

        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        var appId = _appId;

        _ = Task.Run(async () =>
        {
            var result = await _downloader.DownloadAsync(appId, destination, branch, password, token);

            if (result != null)
                RegisterInstall(result);
        });
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!IsActive)
            return;

        StatusText = "Отмена загрузки…";
        _cancellation?.Cancel();
    }

    private void RegisterInstall(DownloadResult result)
    {
        var launch = GameLauncher.Pick(result.LaunchOptions);

        Dispatcher.UIThread.Post(() => _installs.Add(new InstalledApp(
            result.AppId,
            result.AppName,
            result.Branch,
            result.DestinationPath,
            result.TotalBytes,
            DateTime.Now,
            launch?.Executable,
            launch?.Arguments)));
    }

    private async Task LoadBranchesAsync(uint appId)
    {
        IsLoadingBranches = true;

        try
        {
            using var downloader = _session.CreateDownloader();
            var branches = await downloader.GetBranchesAsync(appId, CancellationToken.None);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsPicking || _appId != appId)
                    return;

                foreach (var branch in branches)
                    Branches.Add(new BranchOption(branch));

                SelectedBranch = Branches.FirstOrDefault(b => b.Info.Name == "public") ?? Branches.FirstOrDefault();
                StatusText = Branches.Count == 0 ? "Ветки не найдены" : string.Empty;
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (IsPicking)
                    StatusText = $"Ошибка: {ex.Message}";
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoadingBranches = false);
        }
    }

    private static string BuildDestination(uint appId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Miseria",
        "games",
        appId.ToString());

    private void OnStarted(DownloadStart start)
    {
        IsPreparing = false;
        DestinationText = start.DestinationPath;
        ProgressText = $"0 Б из {FormatBytes(start.TotalBytes)}";
        StatusText = string.Empty;
    }

    private void OnProgress(DownloadProgress progress)
    {
        StatusText = progress.FileName;
        ProgressPercent = progress.TotalBytes > 0 ? progress.DoneBytes * 100.0 / progress.TotalBytes : 0;
        ProgressText = $"{FormatBytes(progress.DoneBytes)} из {FormatBytes(progress.TotalBytes)}";
        SpeedText = $"{FormatBytes((ulong)progress.BytesPerSecond)}/с";
    }

    private void OnFinished(DownloadFinish finish)
    {
        IsPreparing = false;
        IsActive = false;
        SpeedText = string.Empty;

        switch (finish.Kind)
        {
            case DownloadFinishKind.Completed:
                ProgressPercent = 100;
                ProgressText = $"{FormatBytes(finish.TotalBytes)} загружено";
                StatusText = "Загрузка завершена";
                break;
            case DownloadFinishKind.Cancelled:
                StatusText = "Загрузка отменена";
                break;
            case DownloadFinishKind.Failed:
                StatusText = $"Ошибка: {finish.Message}";
                break;
        }
    }

    private static string FormatBytes(ulong bytes)
    {
        double value = bytes;
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{(value >= 100 || unit == 0 ? value.ToString("F0") : value.ToString("F1"))} {units[unit]}";
    }
}

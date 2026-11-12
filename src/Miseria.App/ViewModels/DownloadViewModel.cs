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

public partial class UpdateItem : ObservableObject
{
    public UpdateItem(uint appId, string name, string status, bool isAvailable)
    {
        AppId = appId;
        Name = name;
        _statusText = status;
        _isUpdateAvailable = isAvailable;
    }

    public uint AppId { get; }

    public string Name { get; }

    [ObservableProperty]
    private string _statusText;

    [ObservableProperty]
    private bool _isUpdateAvailable;
}

public partial class DownloadViewModel : ObservableObject
{
    private readonly SteamSession _session;
    private readonly InstallStore _installs;
    private readonly ManifestStore _manifests;
    private SteamDownloader? _downloader;
    private CancellationTokenSource? _cancellation;
    private uint _appId;
    private string? _usedPassword;
    private bool _isUpdateRun;

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
    private bool _hasInstalls;

    [ObservableProperty]
    private bool _isCheckingUpdates;

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
    private string _updatesStatus = string.Empty;

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private BranchOption? _selectedBranch;

    [ObservableProperty]
    private string _branchPassword = string.Empty;

    public ObservableCollection<BranchOption> Branches { get; } = [];

    public ObservableCollection<UpdateItem> Updates { get; } = [];

    public bool NeedsPassword => SelectedBranch?.Info.PasswordRequired == true;

    public bool HasDestination => DestinationText.Length > 0;

    public event Action? Started;

    public DownloadViewModel(SteamSession session, InstallStore installs, ManifestStore manifests)
    {
        _session = session;
        _installs = installs;
        _manifests = manifests;
        _hasInstalls = installs.Installs.Count > 0;

        _installs.Changed += () => Dispatcher.UIThread.Post(() => HasInstalls = _installs.Installs.Count > 0);
        _session.LoggedIn += account => Dispatcher.UIThread.Post(() =>
        {
            HasInstalls = _installs.Installs.Count > 0;

            if (HasInstalls)
                _ = CheckUpdatesAsync();
        });
    }

    partial void OnDestinationTextChanged(string value) => OnPropertyChanged(nameof(HasDestination));

    partial void OnSelectedBranchChanged(BranchOption? value) => OnPropertyChanged(nameof(NeedsPassword));

    [RelayCommand]
    private void Start(AppItem app)
    {
        if (app == null)
            return;

        _appId = app.AppId;
        _usedPassword = null;
        _isUpdateRun = false;
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

        _usedPassword = password;
        _isUpdateRun = false;
        IsPicking = false;
        IsActive = true;
        IsPreparing = true;
        StatusText = "Получение манифестов";
        ProgressText = string.Empty;
        SpeedText = string.Empty;
        DestinationText = string.Empty;
        ProgressPercent = 0;

        RunDownload(null, BuildDestination(_appId), branch);
    }

    [RelayCommand]
    private void UpdateApp(UpdateItem? item)
    {
        if (item == null || IsActive || IsCheckingUpdates)
            return;

        var install = _installs.Installs.FirstOrDefault(i => i.AppId == item.AppId);
        var record = install == null ? null : _manifests.TryGet(item.AppId);

        if (install == null || record == null)
        {
            item.StatusText = "Нет сохранённых манифестов — переустановите игру";
            return;
        }

        _appId = item.AppId;
        _usedPassword = install.BetaPassword;
        _isUpdateRun = true;
        HasContent = true;
        IsPicking = false;
        IsActive = true;
        IsPreparing = true;
        AppName = item.Name;
        StatusText = "Получение манифестов";
        ProgressText = string.Empty;
        SpeedText = string.Empty;
        DestinationText = string.Empty;
        ProgressPercent = 0;

        RunDownload(record, install.Directory, install.Branch);
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        if (IsCheckingUpdates || IsActive)
            return;

        IsCheckingUpdates = true;
        UpdatesStatus = string.Empty;

        try
        {
            var installs = _installs.Installs.ToList();
            var checks = await Task.Run(async () =>
            {
                var results = new List<(InstalledApp Install, UpdateCheck Check)>();

                using var downloader = _session.CreateDownloader();

                foreach (var install in installs)
                {
                    var record = _manifests.TryGet(install.AppId);
                    UpdateCheck check;

                    if (record == null)
                    {
                        check = new UpdateCheck(install.AppId, install.Name, install.Branch, UpdateStatus.Failed, 0, 0, 0, "Нет сохранённых манифестов");
                    }
                    else
                    {
                        try
                        {
                            check = await downloader.CheckUpdateAsync(install.AppId, record, install.BetaPassword, CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            check = new UpdateCheck(install.AppId, install.Name, install.Branch, UpdateStatus.Failed, 0, 0, 0, ex.Message);
                        }
                    }

                    results.Add((install, check));
                }

                return results;
            });

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Updates.Clear();

                foreach (var (install, check) in checks)
                    Updates.Add(BuildUpdateItem(install, check));

                UpdatesStatus = checks.Count == 0 ? string.Empty : $"Проверено игр: {checks.Count}";
            });
        }
        catch
        {
            await Dispatcher.UIThread.InvokeAsync(() => UpdatesStatus = "Не удалось проверить обновления");
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!IsActive)
            return;

        StatusText = "Отмена загрузки…";
        _cancellation?.Cancel();
    }

    private void RunDownload(ManifestRecord? previous, string destination, string branch)
    {
        _downloader?.Dispose();
        _downloader = _session.CreateDownloader();
        _downloader.Started += start => Dispatcher.UIThread.Post(() => OnStarted(start));
        _downloader.Progress += progress => Dispatcher.UIThread.Post(() => OnProgress(progress));
        _downloader.Finished += finish => Dispatcher.UIThread.Post(() => OnFinished(finish));

        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;
        var appId = _appId;
        var password = _usedPassword;
        var downloader = _downloader;

        _ = Task.Run(async () =>
        {
            var result = await downloader.DownloadAsync(appId, destination, branch, password, token, previous);

            if (result == null)
                return;

            _manifests.Save(result.Manifest);
            RegisterInstall(result);
            await RefreshUpdateItemAsync(appId);
        });
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
            launch?.Arguments,
            _usedPassword)));
    }

    private async Task RefreshUpdateItemAsync(uint appId)
    {
        try
        {
            var install = _installs.Installs.FirstOrDefault(i => i.AppId == appId);
            var record = install == null ? null : _manifests.TryGet(appId);
            UpdateItem item;

            if (install == null || record == null)
            {
                item = new UpdateItem(appId, install?.Name ?? appId.ToString(), "Не удалось проверить обновления", false);
            }
            else
            {
                using var downloader = _session.CreateDownloader();
                UpdateCheck check;

                try
                {
                    check = await downloader.CheckUpdateAsync(appId, record, install.BetaPassword, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    check = new UpdateCheck(appId, install.Name, install.Branch, UpdateStatus.Failed, 0, 0, 0, ex.Message);
                }

                item = BuildUpdateItem(install, check);
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var existing = Updates.FirstOrDefault(u => u.AppId == appId);

                if (existing != null)
                {
                    existing.StatusText = item.StatusText;
                    existing.IsUpdateAvailable = item.IsUpdateAvailable;
                }
                else
                {
                    Updates.Add(item);
                }
            });
        }
        catch
        {
        }
    }

    private static UpdateItem BuildUpdateItem(InstalledApp install, UpdateCheck check) => check.Status switch
    {
        UpdateStatus.UpToDate => new UpdateItem(check.AppId, install.Name, $"Актуальная версия · ветка {check.Branch}", false),
        UpdateStatus.Available => new UpdateItem(check.AppId, install.Name, DescribeUpdate(check), true),
        UpdateStatus.PasswordRequired => new UpdateItem(check.AppId, install.Name, $"Обновление доступно, но ветка {check.Branch} защищена паролем", false),
        _ => new UpdateItem(check.AppId, install.Name, $"Не удалось проверить: {check.Message}", false)
    };

    private static string DescribeUpdate(UpdateCheck check)
    {
        var parts = new List<string> { "Обновление доступно" };

        if (check.DownloadBytes > 0)
            parts.Add(FormatBytes(check.DownloadBytes));

        if (check.ChangedFiles > 0)
            parts.Add($"файлов: {check.ChangedFiles}");

        if (check.RemovedFiles > 0)
            parts.Add($"на удаление: {check.RemovedFiles}");

        return string.Join(" · ", parts);
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
                StatusText = _isUpdateRun ? "Обновление установлено" : "Загрузка завершена";
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

using System.IO;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class DownloadViewModel : ObservableObject
{
    private readonly SteamSession _session;
    private SteamDownloader? _downloader;
    private CancellationTokenSource? _cancellation;

    [ObservableProperty]
    private bool _hasContent;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isPreparing;

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
    private double _progressPercent;

    public bool HasDestination => DestinationText.Length > 0;

    public event Action? Started;

    public DownloadViewModel(SteamSession session)
    {
        _session = session;
    }

    partial void OnDestinationTextChanged(string value) => OnPropertyChanged(nameof(HasDestination));

    [RelayCommand]
    private void Start(AppItem app)
    {
        if (IsActive || app == null)
            return;

        HasContent = true;
        IsActive = true;
        IsPreparing = true;
        AppName = app.Name;
        StatusText = "Получение манифестов";
        ProgressText = string.Empty;
        SpeedText = string.Empty;
        DestinationText = string.Empty;
        ProgressPercent = 0;

        var destination = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Miseria",
            "games",
            app.AppId.ToString());

        _downloader = _session.CreateDownloader();
        _downloader.Started += start => Dispatcher.UIThread.Post(() => OnStarted(start));
        _downloader.Progress += progress => Dispatcher.UIThread.Post(() => OnProgress(progress));
        _downloader.Finished += finish => Dispatcher.UIThread.Post(() => OnFinished(finish));

        _cancellation = new CancellationTokenSource();
        _ = Task.Run(() => _downloader.DownloadAsync(app.AppId, destination, _cancellation.Token));

        Started?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!IsActive)
            return;

        StatusText = "Отмена загрузки…";
        _cancellation?.Cancel();
    }

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

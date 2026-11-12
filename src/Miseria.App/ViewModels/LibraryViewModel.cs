using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miseria.App.Services;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private static readonly TimeSpan LaunchMessageLifetime = TimeSpan.FromSeconds(6);

    private readonly SteamSession _session;
    private readonly AppIconCache _icons;
    private readonly InstallStore _installs;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _launchMessageTimer;

    public DownloadViewModel Download { get; }

    public ObservableCollection<AppItem> Apps { get; } = [];

    public ObservableCollection<AppItem> Visible { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hasList;

    [ObservableProperty]
    private string _launchMessage = string.Empty;

    [ObservableProperty]
    private bool _isGrid;

    public bool HasApps => Apps.Count > 0;

    public bool SearchActive => SearchText.Trim().Length > 0;

    public string CountText => Apps.Count == 0
        ? "Загрузка содержимого из Steam"
        : $"Приложений: {Apps.Count}";

    public LibraryViewModel(SteamSession session, AppIconCache icons, InstallStore installs, DownloadViewModel download)
    {
        _session = session;
        _icons = icons;
        _installs = installs;
        Download = download;

        _session.LibraryLoaded += apps => Dispatcher.UIThread.Post(() => ReplaceApps(apps));
        _session.LoggedOut += () => Dispatcher.UIThread.Post(Clear);
        _installs.Changed += () => Dispatcher.UIThread.Post(RefreshInstalls);

        Apps.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasApps));
            OnPropertyChanged(nameof(CountText));
        };

        _refreshTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(120),
            DispatcherPriority.Background,
            OnRefreshTick);

        _launchMessageTimer = new DispatcherTimer { Interval = LaunchMessageLifetime };
        _launchMessageTimer.Tick += (_, _) =>
        {
            _launchMessageTimer.Stop();
            LaunchMessage = string.Empty;
        };
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(SearchActive));
        ScheduleRefresh();
    }

    public void ReplaceApps(IReadOnlyList<AppSnapshot> apps)
    {
        _refreshTimer.Stop();
        Apps.Clear();

        foreach (var snapshot in apps)
        {
            var item = new AppItem(snapshot)
            {
                IsInstalled = _installs.TryGet(snapshot.AppId, out _)
            };
            Apps.Add(item);
            _ = LoadIconAsync(item);
        }

        HasList = true;
        RebuildVisible();

        if (IsGrid)
            LoadCapsules();
    }

    [RelayCommand]
    private void ToggleView()
    {
        IsGrid = !IsGrid;

        if (IsGrid)
            LoadCapsules();
    }

    private void LoadCapsules()
    {
        foreach (var item in Apps)
            _ = LoadCapsuleAsync(item);
    }

    private async Task LoadCapsuleAsync(AppItem item)
    {
        var bitmap = await _icons.LoadCapsuleAsync(item.AppId);

        if (bitmap == null)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (Apps.Contains(item))
            {
                item.Capsule = bitmap;
                item.HasCapsule = true;
            }
        });
    }

    private async Task LoadIconAsync(AppItem item)
    {
        var bitmap = await _icons.LoadAsync(item.AppId, item.IconHash);

        if (bitmap == null)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (item.IconHash != null)
            {
                item.Icon = bitmap;
                item.HasIcon = true;
            }
        });
    }

    public void Clear()
    {
        _refreshTimer.Stop();
        Apps.Clear();
        Visible.Clear();
        HasList = false;
        IsGrid = false;
    }

    [RelayCommand]
    private void Play(AppItem? app)
    {
        if (app == null || !_installs.TryGet(app.AppId, out var install))
            return;

        try
        {
            GameLauncher.Launch(install);
            LaunchMessage = $"Запуск {app.Name}…";
        }
        catch (Exception ex)
        {
            LaunchMessage = $"Не удалось запустить: {ex.Message}";
        }

        _launchMessageTimer.Stop();
        _launchMessageTimer.Start();
    }

    private void RefreshInstalls()
    {
        foreach (var item in Apps)
            item.IsInstalled = _installs.TryGet(item.AppId, out _);
    }

    private void ScheduleRefresh()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        if (sender is DispatcherTimer timer)
            timer.Stop();

        RebuildVisible();
    }

    private void RebuildVisible()
    {
        var query = SearchText.Trim();

        var items = Apps
            .Where(a => query.Length == 0 || a.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase);

        Visible.Clear();

        foreach (var item in items)
            Visible.Add(item);
    }
}

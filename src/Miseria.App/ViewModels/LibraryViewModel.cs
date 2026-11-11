using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Miseria.App.Services;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class LibraryViewModel : ObservableObject
{
    private readonly SteamSession _session;
    private readonly AppIconCache _icons;
    private readonly DispatcherTimer _refreshTimer;

    public ObservableCollection<AppItem> Apps { get; } = [];

    public ObservableCollection<AppItem> Visible { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hasList;

    public bool HasApps => Apps.Count > 0;

    public bool SearchActive => SearchText.Trim().Length > 0;

    public string CountText => Apps.Count == 0
        ? "Загрузка содержимого из Steam"
        : $"Приложений: {Apps.Count}";

    public LibraryViewModel(SteamSession session, AppIconCache icons)
    {
        _session = session;
        _icons = icons;

        _session.LibraryLoaded += apps => Dispatcher.UIThread.Post(() => ReplaceApps(apps));
        _session.LoggedOut += () => Dispatcher.UIThread.Post(Clear);

        Apps.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasApps));
            OnPropertyChanged(nameof(CountText));
        };

        _refreshTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(120),
            DispatcherPriority.Background,
            OnRefreshTick);
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
            var item = new AppItem(snapshot);
            Apps.Add(item);
            _ = LoadIconAsync(item);
        }

        HasList = true;
        RebuildVisible();
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

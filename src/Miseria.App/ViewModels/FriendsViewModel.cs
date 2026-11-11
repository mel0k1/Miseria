using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Miseria.App.Services;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class FriendsViewModel : ObservableObject
{
    private readonly SteamSession _session;
    private readonly AvatarCache _avatars;
    private readonly Dictionary<ulong, FriendItem> _index = [];
    private readonly DispatcherTimer _refreshTimer;

    public ObservableCollection<FriendItem> Friends { get; } = [];

    public ObservableCollection<FriendItem> Visible { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hasList;

    public bool HasFriends => Friends.Count > 0;

    public bool SearchActive => SearchText.Trim().Length > 0;

    public FriendsViewModel(SteamSession session, AvatarCache avatars)
    {
        _session = session;
        _avatars = avatars;

        _session.FriendsLoaded += ids => Dispatcher.UIThread.Post(() => ReplaceFriends(ids));
        _session.FriendUpdated += snapshot => Dispatcher.UIThread.Post(() => ApplySnapshot(snapshot));
        _session.LoggedOut += () => Dispatcher.UIThread.Post(Clear);

        Friends.CollectionChanged += OnFriendsChanged;

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

    public void ReplaceFriends(IReadOnlyList<ulong> ids)
    {
        _refreshTimer.Stop();
        _index.Clear();
        Friends.Clear();

        foreach (var id in ids)
        {
            var item = new FriendItem(id);
            _index[id] = item;
            Friends.Add(item);
        }

        HasList = true;
        RebuildVisible();
    }

    public void ApplySnapshot(FriendSnapshot snapshot)
    {
        if (!_index.TryGetValue(snapshot.SteamId, out var item))
        {
            item = new FriendItem(snapshot.SteamId);
            _index[snapshot.SteamId] = item;
            Friends.Add(item);
        }

        item.UpdateFrom(snapshot);
        _ = LoadAvatarAsync(item, snapshot.AvatarHash);
        ScheduleRefresh();
    }

    public void Clear()
    {
        _refreshTimer.Stop();
        _index.Clear();
        Friends.Clear();
        Visible.Clear();
        HasList = false;
    }

    private void OnFriendsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(HasFriends));

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

        var items = Friends
            .Where(f => query.Length == 0 || f.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => Rank(f.Presence))
            .ThenBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase);

        Visible.Clear();

        foreach (var item in items)
            Visible.Add(item);
    }

    private async Task LoadAvatarAsync(FriendItem item, string? hash)
    {
        var bitmap = await _avatars.LoadAsync(hash);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (item.AvatarHash == hash)
            {
                item.Avatar = bitmap;
                item.HasAvatar = bitmap != null;
            }
        });
    }

    private static int Rank(FriendPresence presence) => presence switch
    {
        FriendPresence.InGame => 0,
        FriendPresence.Online or FriendPresence.Busy
            or FriendPresence.LookingToTrade or FriendPresence.LookingToPlay => 1,
        FriendPresence.Away or FriendPresence.Snooze => 2,
        _ => 3
    };
}

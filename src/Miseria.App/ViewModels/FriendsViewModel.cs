using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miseria.App.Services;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class FriendsViewModel : ObservableObject
{
    private readonly SteamSession _session;
    private readonly AvatarCache _avatars;
    private readonly Dictionary<ulong, FriendItem> _index = [];
    private readonly Dictionary<ulong, List<ChatBubble>> _conversations = [];
    private readonly HashSet<ulong> _historyRequested = [];
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _typingTimer;
    private FriendItem? _statusSubscription;

    public ObservableCollection<FriendItem> Friends { get; } = [];

    public ObservableCollection<FriendItem> Visible { get; } = [];

    public ObservableCollection<ChatBubble> Messages { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hasList;

    [ObservableProperty]
    private string _draft = string.Empty;

    [ObservableProperty]
    private bool _isPeerTyping;

    private FriendItem? _selectedFriend;

    public FriendItem? SelectedFriend => _selectedFriend;

    public event Action<FriendItem>? ProfileRequested;

    public bool HasFriends => Friends.Count > 0;

    public bool SearchActive => SearchText.Trim().Length > 0;

    public bool HasPeer => SelectedFriend != null;

    public bool HasMessages => Messages.Count > 0;

    public string PeerStatusText => IsPeerTyping
        ? "Печатает…"
        : SelectedFriend?.PresenceText ?? string.Empty;

    public FriendsViewModel(SteamSession session, AvatarCache avatars)
    {
        _session = session;
        _avatars = avatars;

        _session.FriendsLoaded += ids => Dispatcher.UIThread.Post(() => ReplaceFriends(ids));
        _session.FriendUpdated += snapshot => Dispatcher.UIThread.Post(() => ApplySnapshot(snapshot));
        _session.LoggedOut += () => Dispatcher.UIThread.Post(Clear);
        _session.ChatMessageReceived += message => Dispatcher.UIThread.Post(() => OnChatMessage(message));
        _session.FriendTyping += steamId => Dispatcher.UIThread.Post(() => OnPeerTyping(steamId));
        _session.ChatHistoryLoaded += (steamId, messages) => Dispatcher.UIThread.Post(() => OnHistoryLoaded(steamId, messages));

        Friends.CollectionChanged += OnFriendsChanged;
        Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasMessages));

        _refreshTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(120),
            DispatcherPriority.Background,
            OnRefreshTick);

        _typingTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(4),
            DispatcherPriority.Background,
            OnTypingTimeout);
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(SearchActive));
        ScheduleRefresh();
    }

    public void OpenChat(FriendItem item)
    {
        if (_selectedFriend == item)
        {
            item.UnreadCount = 0;
            return;
        }

        if (_statusSubscription != null)
            _statusSubscription.PropertyChanged -= OnSelectedPropertyChanged;

        if (_selectedFriend != null)
            _selectedFriend.IsSelected = false;

        _selectedFriend = item;
        item.IsSelected = true;
        _statusSubscription = item;
        item.PropertyChanged += OnSelectedPropertyChanged;

        OnPropertyChanged(nameof(SelectedFriend));
        OnPropertyChanged(nameof(HasPeer));
        OnPropertyChanged(nameof(PeerStatusText));

        item.UnreadCount = 0;
        IsPeerTyping = false;
        ReloadMessages(item.SteamId);

        if (_session.State == SessionState.Online && _historyRequested.Add(item.SteamId))
            _session.RequestChatHistory(item.SteamId);
    }

    [RelayCommand]
    private void OpenProfile()
    {
        if (SelectedFriend != null)
            ProfileRequested?.Invoke(SelectedFriend);
    }

    [RelayCommand]
    private void Send()
    {
        if (SelectedFriend == null)
            return;

        var text = Draft.Trim();

        if (text.Length == 0)
            return;

        var peer = SelectedFriend.SteamId;
        _session.SendChatMessage(peer, text);
        StoreMessage(peer, new ChatBubble(text, DateTimeOffset.UtcNow, true));
        Draft = string.Empty;
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
        _conversations.Clear();
        _historyRequested.Clear();

        if (_statusSubscription != null)
            _statusSubscription.PropertyChanged -= OnSelectedPropertyChanged;

        _statusSubscription = null;

        if (_selectedFriend != null)
            _selectedFriend.IsSelected = false;

        _selectedFriend = null;
        Messages.Clear();
        Draft = string.Empty;
        IsPeerTyping = false;
        HasList = false;
        OnPropertyChanged(nameof(SelectedFriend));
        OnPropertyChanged(nameof(HasPeer));
    }

    private void OnChatMessage(ChatMessage message)
    {
        var selected = SelectedFriend?.SteamId == message.PeerId;
        StoreMessage(message.PeerId, new ChatBubble(message.Text, message.Timestamp, message.AuthorId == _session.OwnSteamId));

        if (!selected && _index.TryGetValue(message.PeerId, out var item))
            item.UnreadCount++;
    }

    private void OnPeerTyping(ulong steamId)
    {
        if (SelectedFriend?.SteamId != steamId)
            return;

        IsPeerTyping = true;
        _typingTimer.Stop();
        _typingTimer.Start();
    }

    private void OnTypingTimeout(object? sender, EventArgs e)
    {
        if (sender is DispatcherTimer timer)
            timer.Stop();

        IsPeerTyping = false;
    }

    private void OnHistoryLoaded(ulong steamId, IReadOnlyList<ChatMessage> messages)
    {
        var bubbles = messages
            .Select(m => new ChatBubble(m.Text, m.Timestamp, m.AuthorId == _session.OwnSteamId))
            .ToList();

        if (_conversations.TryGetValue(steamId, out var live))
            bubbles.AddRange(live);

        bubbles.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        _conversations[steamId] = bubbles;

        if (SelectedFriend?.SteamId == steamId)
            ReloadMessages(steamId);
    }

    private void StoreMessage(ulong steamId, ChatBubble bubble)
    {
        if (!_conversations.TryGetValue(steamId, out var list))
            _conversations[steamId] = list = [];

        list.Add(bubble);

        if (SelectedFriend?.SteamId == steamId)
            Messages.Add(bubble);
    }

    private void ReloadMessages(ulong steamId)
    {
        Messages.Clear();

        if (_conversations.TryGetValue(steamId, out var list))
        {
            foreach (var bubble in list)
                Messages.Add(bubble);
        }
    }

    private void OnSelectedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FriendItem.PresenceText))
            OnPropertyChanged(nameof(PeerStatusText));
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

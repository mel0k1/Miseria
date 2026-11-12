using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miseria.App.Services;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly SteamSession _session;
    private readonly InstallStore _installs;
    private readonly LibraryViewModel _library;
    private readonly FriendsViewModel _friends;

    [ObservableProperty]
    private AccountSnapshot? _account;

    [ObservableProperty]
    private bool _isConnected;

    public int AppsCount { get; private set; }

    public int InstalledCount { get; private set; }

    public int FriendsCount { get; private set; }

    public int FriendsOnlineCount { get; private set; }

    public string SteamIdText => Account == null ? string.Empty : $"Steam ID {Account.SteamId}";

    public HomeViewModel(SteamSession session, LibraryViewModel library, FriendsViewModel friends, InstallStore installs)
    {
        _session = session;
        _installs = installs;
        _library = library;
        _friends = friends;

        _session.LoggedOut += () => Dispatcher.UIThread.Post(() => IsConnected = false);
        _session.FriendUpdated += _ => Dispatcher.UIThread.Post(RefreshFriends);

        _library.Apps.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(RefreshApps);
        _friends.Friends.CollectionChanged += OnFriendsChanged;
        _installs.Changed += () => Dispatcher.UIThread.Post(RefreshApps);
    }

    public void Initialize(AccountSnapshot account)
    {
        Account = account;
        IsConnected = true;
        OnPropertyChanged(nameof(SteamIdText));
        RefreshApps();
        RefreshFriends();
    }

    [RelayCommand]
    private void LogOff() => _session.LogOff();

    private void OnFriendsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(RefreshFriends);

    private void RefreshApps()
    {
        AppsCount = _library.Apps.Count;
        InstalledCount = _library.Apps.Count(a => a.IsInstalled);
        InstalledCount = Math.Max(InstalledCount, _installs.Installs.Count);

        OnPropertyChanged(nameof(AppsCount));
        OnPropertyChanged(nameof(InstalledCount));
    }

    private void RefreshFriends()
    {
        FriendsCount = _friends.Friends.Count;
        FriendsOnlineCount = _friends.Friends.Count(f => f.IsOnline || f.IsInGame);

        OnPropertyChanged(nameof(FriendsCount));
        OnPropertyChanged(nameof(FriendsOnlineCount));
    }
}

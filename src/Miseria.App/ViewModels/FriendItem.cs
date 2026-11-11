using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class FriendItem : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _avatarHash;

    [ObservableProperty]
    private FriendPresence _presence;

    [ObservableProperty]
    private string? _gameName;

    [ObservableProperty]
    private Bitmap? _avatar;

    [ObservableProperty]
    private bool _hasAvatar;

    public ulong SteamId { get; }

    public FriendItem(ulong steamId) => SteamId = steamId;

    public bool IsInGame => Presence == FriendPresence.InGame;

    public bool IsOnline => Presence is FriendPresence.Online or FriendPresence.Busy
        or FriendPresence.LookingToTrade or FriendPresence.LookingToPlay;

    public bool IsAway => Presence is FriendPresence.Away or FriendPresence.Snooze;

    public bool IsOffline => Presence == FriendPresence.Offline;

    public string PresenceText => Presence switch
    {
        FriendPresence.Online => "В сети",
        FriendPresence.Busy => "Не беспокоить",
        FriendPresence.Away => "Отошёл",
        FriendPresence.Snooze => "Спит",
        FriendPresence.LookingToTrade => "Ищет обмен",
        FriendPresence.LookingToPlay => "Ищет игру",
        FriendPresence.InGame => string.IsNullOrWhiteSpace(GameName) ? "В игре" : GameName,
        _ => "Не в сети"
    };

    public string Initial => Name.Length > 0 ? char.ToUpperInvariant(Name[0]).ToString() : "?";

    partial void OnPresenceChanged(FriendPresence value)
    {
        OnPropertyChanged(nameof(IsInGame));
        OnPropertyChanged(nameof(IsOnline));
        OnPropertyChanged(nameof(IsAway));
        OnPropertyChanged(nameof(IsOffline));
        OnPropertyChanged(nameof(PresenceText));
    }

    partial void OnGameNameChanged(string? value) => OnPropertyChanged(nameof(PresenceText));

    public void UpdateFrom(FriendSnapshot snapshot)
    {
        Name = snapshot.Name;
        AvatarHash = snapshot.AvatarHash;
        Presence = snapshot.Presence;
        GameName = snapshot.GameName;
    }
}

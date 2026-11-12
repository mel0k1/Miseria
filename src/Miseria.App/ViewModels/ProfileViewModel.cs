using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Miseria.App.Services;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    private readonly SteamSession _session;
    private readonly AvatarCache _avatars;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string? _avatarHash;

    [ObservableProperty]
    private Bitmap? _avatar;

    [ObservableProperty]
    private bool _hasAvatar;

    [ObservableProperty]
    private FriendPresence? _presence;

    [ObservableProperty]
    private string? _gameName;

    [ObservableProperty]
    private string _accountName = string.Empty;

    [ObservableProperty]
    private string? _realName;

    [ObservableProperty]
    private string? _country;

    [ObservableProperty]
    private string? _summary;

    [ObservableProperty]
    private DateTimeOffset? _memberSince;

    [ObservableProperty]
    private ulong _steamId;

    public ProfileViewModel(SteamSession session, AvatarCache avatars)
    {
        _session = session;
        _avatars = avatars;

        _session.ProfileUpdated += snapshot => Dispatcher.UIThread.Post(() => Apply(snapshot));
        _session.LoggedIn += account => Dispatcher.UIThread.Post(() => Initialize(account));
        _session.LoggedOut += () => Dispatcher.UIThread.Post(Reset);
    }

    public bool HasRealName => !string.IsNullOrWhiteSpace(RealName);

    public bool HasCountry => !string.IsNullOrWhiteSpace(Country);

    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);

    public bool HasMemberSince => MemberSince != null;

    public bool IsOnline => Presence is FriendPresence.Online or FriendPresence.Busy
        or FriendPresence.LookingToTrade or FriendPresence.LookingToPlay;

    public bool IsAway => Presence is FriendPresence.Away or FriendPresence.Snooze;

    public bool IsInGame => Presence == FriendPresence.InGame;

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
        FriendPresence.Offline => "Не в сети",
        _ => string.Empty
    };

    public string SteamIdText => SteamId == 0 ? string.Empty : $"Steam ID {SteamId}";

    public string ProfileUrl => $"https://steamcommunity.com/profiles/{SteamId}";

    private static readonly string[] Months =
    [
        "января", "февраля", "марта", "апреля", "мая", "июня",
        "июля", "августа", "сентября", "октября", "ноября", "декабря"
    ];

    public string MemberSinceText
    {
        get
        {
            if (MemberSince == null)
                return string.Empty;

            var local = MemberSince.Value.ToLocalTime();
            return $"{local.Day} {Months[local.Month - 1]} {local.Year}";
        }
    }

    public string Initial => Name.Length > 0 ? char.ToUpperInvariant(Name[0]).ToString() : "?";

    public void Initialize(AccountSnapshot account)
    {
        SteamId = account.SteamId;
        AccountName = account.AccountName;
        OnPropertyChanged(nameof(SteamIdText));
        OnPropertyChanged(nameof(ProfileUrl));
    }

    public void ApplySnapshot(ProfileSnapshot snapshot) => Apply(snapshot);

    private void Apply(ProfileSnapshot snapshot)
    {
        if (SteamId == 0)
            SteamId = snapshot.SteamId;

        if (snapshot.SteamId != SteamId)
            return;

        if (snapshot.Name != null)
            Name = snapshot.Name;

        if (snapshot.Presence != null)
            Presence = snapshot.Presence;

        if (snapshot.GameName != null)
            GameName = snapshot.GameName;

        if (snapshot.RealName != null)
            RealName = snapshot.RealName;

        if (snapshot.Country != null)
            Country = snapshot.Country;

        if (snapshot.Summary != null)
            Summary = snapshot.Summary;

        if (snapshot.MemberSince != null)
            MemberSince = snapshot.MemberSince;

        if (snapshot.AvatarHash != null && snapshot.AvatarHash != AvatarHash)
        {
            AvatarHash = snapshot.AvatarHash;
            _ = LoadAvatarAsync(snapshot.AvatarHash);
        }
    }

    private async Task LoadAvatarAsync(string hash)
    {
        var bitmap = await _avatars.LoadAsync(hash);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (AvatarHash == hash)
            {
                Avatar = bitmap;
                HasAvatar = bitmap != null;
            }
        });
    }

    private void Reset()
    {
        Name = string.Empty;
        AvatarHash = null;
        Avatar = null;
        HasAvatar = false;
        Presence = null;
        GameName = null;
        AccountName = string.Empty;
        RealName = null;
        Country = null;
        Summary = null;
        MemberSince = null;
        SteamId = 0;
        OnPropertyChanged(nameof(SteamIdText));
    }

    partial void OnPresenceChanged(FriendPresence? value)
    {
        OnPropertyChanged(nameof(IsOnline));
        OnPropertyChanged(nameof(IsAway));
        OnPropertyChanged(nameof(IsInGame));
        OnPropertyChanged(nameof(IsOffline));
        OnPropertyChanged(nameof(PresenceText));
    }

    partial void OnGameNameChanged(string? value) => OnPropertyChanged(nameof(PresenceText));

    partial void OnRealNameChanged(string? value) => OnPropertyChanged(nameof(HasRealName));

    partial void OnCountryChanged(string? value) => OnPropertyChanged(nameof(HasCountry));

    partial void OnSummaryChanged(string? value) => OnPropertyChanged(nameof(HasSummary));

    partial void OnMemberSinceChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(HasMemberSince));
        OnPropertyChanged(nameof(MemberSinceText));
    }

    partial void OnSteamIdChanged(ulong value)
    {
        OnPropertyChanged(nameof(SteamIdText));
        OnPropertyChanged(nameof(ProfileUrl));
    }
}

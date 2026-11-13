using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miseria.App.Services;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class ProfileViewModel : ObservableObject
{
    private readonly SteamSession _session;
    private readonly AvatarCache _avatars;
    private readonly ItemIconCache _icons;
    private readonly ProfileService _profiles;
    private ProfileSnapshot? _ownSnapshot;
    private string _ownAccountName = string.Empty;
    private ulong _ownSteamId;
    private FriendItem? _friend;

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

    [ObservableProperty]
    private bool _isFriendMode;

    [ObservableProperty]
    private bool _extrasBusy;

    [ObservableProperty]
    private bool _isPrivate;

    [ObservableProperty]
    private int? _level;

    [ObservableProperty]
    private int? _gamesCount;

    [ObservableProperty]
    private double? _totalHours;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public ObservableCollection<ProfileGameRow> TopGames { get; } = [];

    public ObservableCollection<ProfileScreenshotRow> Screenshots { get; } = [];

    public ProfileViewModel(SteamSession session, AvatarCache avatars, ItemIconCache icons, ProfileService profiles)
    {
        _session = session;
        _avatars = avatars;
        _icons = icons;
        _profiles = profiles;

        _session.ProfileUpdated += snapshot => Dispatcher.UIThread.Post(() => Apply(snapshot));
        _session.LoggedIn += account => Dispatcher.UIThread.Post(() => Initialize(account));
        _session.LoggedOut += () => Dispatcher.UIThread.Post(Reset);

        TopGames.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasGames));
        Screenshots.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasScreenshots));
    }

    public bool HasRealName => !string.IsNullOrWhiteSpace(RealName);

    public bool HasCountry => !string.IsNullOrWhiteSpace(Country);

    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);

    public bool HasMemberSince => MemberSince != null;

    public bool HasStatus => StatusText.Length > 0;

    public bool HasLevel => Level != null;

    public bool HasGames => TopGames.Count > 0;

    public bool HasScreenshots => Screenshots.Count > 0;

    public bool HasBack => IsFriendMode;

    public bool HasAccountName => !IsFriendMode && AccountName.Length > 0;

    public string TitleText => IsFriendMode ? "Профиль друга" : "Профиль";

    public string CountryLabel => IsFriendMode ? "Расположение" : "Страна";

    public string LevelText => Level?.ToString() ?? "—";

    public string GamesCountText => GamesCount == null ? "—" : RuFormat.Number(GamesCount.Value);

    public string TotalHoursText => TotalHours is > 0 ? RuFormat.Hours(TotalHours.Value) : "—";

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
        _ownSteamId = account.SteamId;
        _ownAccountName = account.AccountName;
        SteamId = account.SteamId;
        AccountName = account.AccountName;
        OnPropertyChanged(nameof(SteamIdText));
        OnPropertyChanged(nameof(ProfileUrl));

        if (!App.ScreenshotMode)
            _ = LoadExtrasAsync(account.SteamId);
    }

    public void ApplySnapshot(ProfileSnapshot snapshot) => Apply(snapshot);

    public void OpenFriend(FriendItem item)
    {
        if (IsFriendMode && _friend == item)
            return;

        DetachFriend();

        _friend = item;
        _friend.PropertyChanged += OnFriendPropertyChanged;
        IsFriendMode = true;

        _ownAccountName = AccountName.Length > 0 ? AccountName : _ownAccountName;
        AccountName = string.Empty;
        SteamId = item.SteamId;
        Name = item.Name;
        Presence = item.Presence;
        GameName = item.GameName;
        AvatarHash = null;
        Avatar = null;
        HasAvatar = false;
        RealName = null;
        Country = null;
        Summary = null;
        MemberSince = null;
        StatusText = string.Empty;

        OnPropertyChanged(nameof(SteamIdText));
        OnPropertyChanged(nameof(ProfileUrl));

        if (item.AvatarHash != null)
        {
            AvatarHash = item.AvatarHash;
            _ = LoadAvatarAsync(item.AvatarHash);
        }

        ClearExtras();
        _ = LoadExtrasAsync(item.SteamId);
    }

    [RelayCommand]
    private void BackToOwn()
    {
        DetachFriend();
        _friend = null;
        IsFriendMode = false;

        SteamId = _ownSteamId;
        AccountName = _ownAccountName;
        Name = string.Empty;
        AvatarHash = null;
        Avatar = null;
        HasAvatar = false;
        Presence = null;
        GameName = null;
        RealName = null;
        Country = null;
        Summary = null;
        MemberSince = null;
        StatusText = string.Empty;

        OnPropertyChanged(nameof(SteamIdText));
        OnPropertyChanged(nameof(ProfileUrl));

        if (_ownSnapshot != null)
            Apply(_ownSnapshot);

        ClearExtras();
        _ = LoadExtrasAsync(_ownSteamId);
    }

    private void Apply(ProfileSnapshot snapshot)
    {
        if (_ownSteamId != 0 && snapshot.SteamId == _ownSteamId)
            _ownSnapshot = snapshot;

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

    private void OnFriendPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!IsFriendMode || _friend == null)
            return;

        switch (e.PropertyName)
        {
            case nameof(FriendItem.Name):
                Name = _friend.Name;
                break;

            case nameof(FriendItem.Presence):
                Presence = _friend.Presence;
                break;

            case nameof(FriendItem.GameName):
                GameName = _friend.GameName;
                break;

            case nameof(FriendItem.Avatar):
                Avatar = _friend.Avatar;
                HasAvatar = _friend.HasAvatar;
                break;
        }
    }

    private async Task LoadExtrasAsync(ulong steamId)
    {
        ExtrasBusy = true;

        try
        {
            var profile = await _profiles.LoadAsync(steamId, steamId == _ownSteamId);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (SteamId != steamId)
                    return;

                ApplyExtras(profile);
            });
        }
        catch (HttpRequestException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (SteamId == steamId)
                    StatusText = "Не удалось загрузить данные профиля";
            });
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (SteamId == steamId)
                    ExtrasBusy = false;
            });
        }
    }

    private void ApplyExtras(FriendProfile? profile)
    {
        if (profile == null)
            return;

        IsPrivate = profile.IsPrivate;
        Level = profile.Level;
        GamesCount = profile.GamesCount;
        TotalHours = profile.TotalHours > 0 ? profile.TotalHours : null;

        RealName ??= profile.RealName;
        Country ??= profile.Location;
        Summary ??= profile.Summary;
        MemberSince ??= profile.MemberSince;

        foreach (var game in profile.Games)
            TopGames.Add(new ProfileGameRow(game.Name, RuFormat.Hours(game.Hours)));

        foreach (var screenshot in profile.Screenshots)
        {
            var row = new ProfileScreenshotRow(screenshot.PreviewUrl, screenshot.DetailsUrl, _icons);
            Screenshots.Add(row);
            _ = row.LoadAsync();
        }
    }

    private void ClearExtras()
    {
        IsPrivate = false;
        Level = null;
        GamesCount = null;
        TotalHours = null;

        TopGames.Clear();
        Screenshots.Clear();
    }

    private void DetachFriend()
    {
        if (_friend != null)
            _friend.PropertyChanged -= OnFriendPropertyChanged;
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
        DetachFriend();
        _friend = null;
        _ownSnapshot = null;
        _ownAccountName = string.Empty;
        _ownSteamId = 0;
        IsFriendMode = false;
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
        StatusText = string.Empty;
        ClearExtras();
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

    partial void OnIsFriendModeChanged(bool value)
    {
        OnPropertyChanged(nameof(TitleText));
        OnPropertyChanged(nameof(HasBack));
        OnPropertyChanged(nameof(HasAccountName));
        OnPropertyChanged(nameof(CountryLabel));
    }

    partial void OnLevelChanged(int? value)
    {
        OnPropertyChanged(nameof(HasLevel));
        OnPropertyChanged(nameof(LevelText));
    }

    partial void OnGamesCountChanged(int? value) => OnPropertyChanged(nameof(GamesCountText));

    partial void OnTotalHoursChanged(double? value) => OnPropertyChanged(nameof(TotalHoursText));

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(HasStatus));
}

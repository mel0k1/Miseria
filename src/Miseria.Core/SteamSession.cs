using System.Net.Http;
using System.Net.Sockets;
using SteamKit2;
using SteamKit2.Authentication;

namespace Miseria.Core;

public sealed class SteamSession : IDisposable
{
    private readonly SteamClient _client;
    private readonly CallbackManager _manager;
    private readonly SteamUser _user;
    private readonly SteamFriends _friends;
    private readonly SteamApps _apps;
    private readonly SessionStore _store;
    private readonly CancellationTokenSource _pumpCts = new();
    private readonly HashSet<ulong> _friendIds = [];
    private int _libraryBusy;

    private TaskCompletionSource _connected = NewCompletion();
    private string? _guardData;
    private string? _accountName;
    private StoredSession? _activeSession;
    private bool _tokenLogon;

    public event Action<SessionState>? StateChanged;
    public event Action<GuardPrompt, TaskCompletionSource<string?>>? GuardRequested;
    public event Action<AccountSnapshot>? LoggedIn;
    public event Action<LogonFailure>? LogonFailed;
    public event Action? LoggedOut;
    public event Action<IReadOnlyList<ulong>>? FriendsLoaded;
    public event Action<FriendSnapshot>? FriendUpdated;
    public event Action<IReadOnlyList<AppSnapshot>>? LibraryLoaded;

    public SessionState State { get; private set; } = SessionState.Disconnected;

    public SteamSession(SessionStore store)
    {
        _store = store;
        _guardData = store.TryLoad()?.GuardData;

        _client = new SteamClient();
        _manager = new CallbackManager(_client);
        _user = _client.GetHandler<SteamUser>()!;
        _friends = _client.GetHandler<SteamFriends>()!;
        _apps = _client.GetHandler<SteamApps>()!;

        _manager.Subscribe<SteamClient.ConnectedCallback>(_ => _connected.TrySetResult());
        _manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
        _manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        _manager.Subscribe<SteamFriends.FriendsListCallback>(OnFriendsList);
        _manager.Subscribe<SteamFriends.PersonaStateCallback>(OnPersonaState);
        _manager.Subscribe<SteamApps.LicenseListCallback>(OnLicenseList);

        _ = Task.Run(PumpLoop);
    }

    public async Task LogOnAsync(LogonRequest request, CancellationToken cancellationToken = default)
    {
        if (State == SessionState.Online)
            throw new InvalidOperationException("Session is already online.");

        _tokenLogon = false;
        SetState(SessionState.Connecting);

        try
        {
            await EnsureConnectedAsync(cancellationToken);

            SetState(SessionState.Authenticating);

            var authSession = await _client.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
            {
                Username = request.Username,
                Password = request.Password,
                IsPersistentSession = true,
                GuardData = _guardData,
                Authenticator = new InteractiveAuthenticator(AskGuard)
            });

            var poll = await authSession.PollingWaitForResultAsync(cancellationToken);

            _accountName = poll.AccountName;

            if (poll.NewGuardData != null)
                _guardData = poll.NewGuardData;

            _activeSession = new StoredSession(poll.AccountName, 0, poll.RefreshToken, _guardData, DateTimeOffset.UtcNow);

            _user.LogOn(new SteamUser.LogOnDetails
            {
                Username = poll.AccountName,
                AccessToken = poll.RefreshToken,
                ShouldRememberPassword = false
            });
        }
        catch (OperationCanceledException)
        {
            SetState(SessionState.Disconnected);
            LogonFailed?.Invoke(new LogonFailure(LogonFailureReason.Cancelled, string.Empty));
        }
        catch (GuardCancelledException)
        {
            SetState(SessionState.Disconnected);
            LogonFailed?.Invoke(new LogonFailure(LogonFailureReason.Cancelled, string.Empty));
        }
        catch (AuthenticationException ex)
        {
            SetState(SessionState.Disconnected);
            LogonFailed?.Invoke(new LogonFailure(Map(ex.Result), ex.Result.ToString()));
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or TimeoutException)
        {
            SetState(SessionState.Disconnected);
            LogonFailed?.Invoke(new LogonFailure(LogonFailureReason.ConnectionFailed, ex.Message));
        }
    }

    public async Task LogOnWithTokenAsync(StoredSession session, CancellationToken cancellationToken = default)
    {
        if (State == SessionState.Online)
            throw new InvalidOperationException("Session is already online.");

        _tokenLogon = true;
        _activeSession = session;
        SetState(SessionState.Connecting);

        try
        {
            await EnsureConnectedAsync(cancellationToken);

            SetState(SessionState.Authenticating);

            _accountName = session.AccountName;

            _user.LogOn(new SteamUser.LogOnDetails
            {
                Username = session.AccountName,
                AccessToken = session.RefreshToken,
                ShouldRememberPassword = false
            });
        }
        catch (OperationCanceledException)
        {
            SetState(SessionState.Disconnected);
            LogonFailed?.Invoke(new LogonFailure(LogonFailureReason.Cancelled, string.Empty));
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or TimeoutException)
        {
            SetState(SessionState.Disconnected);
            LogonFailed?.Invoke(new LogonFailure(LogonFailureReason.ConnectionFailed, ex.Message));
        }
    }

    public void LogOff()
    {
        if (State != SessionState.Online)
            return;

        _store.Delete();
        _user.LogOff();
    }

    public void Dispose()
    {
        _pumpCts.Cancel();
        _client.Disconnect();
        _pumpCts.Dispose();
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_client.IsConnected)
            return;

        _connected = NewCompletion();
        _client.Connect();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await _connected.Task.WaitAsync(timeout.Token);
    }

    private Task<string?> AskGuard(GuardPrompt prompt)
    {
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        GuardRequested?.Invoke(prompt, completion);
        return completion.Task;
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback callback)
    {
        if (callback.Result != EResult.OK)
        {
            var invalidSession = _tokenLogon && callback.Result is EResult.InvalidPassword or EResult.Expired or EResult.AccessDenied;

            if (invalidSession)
                _store.Delete();

            SetState(SessionState.Disconnected);
            LogonFailed?.Invoke(new LogonFailure(
                invalidSession ? LogonFailureReason.InvalidSession : Map(callback.Result),
                callback.Result.ToString()));
            return;
        }

        SetState(SessionState.Online);

        var steamId = callback.ClientSteamID?.ConvertToUInt64() ?? 0;

        if (_activeSession != null)
            _store.Save(_activeSession with { SteamId = steamId, GuardData = _guardData });

        _friends.SetPersonaState(EPersonaState.Online);

        LoggedIn?.Invoke(new AccountSnapshot(
            _accountName ?? string.Empty,
            steamId,
            callback.IPCountryCode));
    }

    private void OnFriendsList(SteamFriends.FriendsListCallback callback)
    {
        if (callback.Incremental)
            return;

        _friendIds.Clear();

        foreach (var friend in callback.FriendList)
        {
            if (friend.Relationship == EFriendRelationship.Friend && friend.SteamID.IsIndividualAccount)
                _friendIds.Add(friend.SteamID);
        }

        FriendsLoaded?.Invoke(_friendIds.ToArray());
    }

    private void OnPersonaState(SteamFriends.PersonaStateCallback callback)
    {
        if (!_friendIds.Contains(callback.FriendID))
            return;

        FriendUpdated?.Invoke(BuildFriend(callback.FriendID));
    }

    private FriendSnapshot BuildFriend(SteamID id)
    {
        var state = _friends.GetFriendPersonaState(id);
        var game = _friends.GetFriendGamePlayed(id);
        var gameName = _friends.GetFriendGamePlayedName(id);
        var inGame = game.ToUInt64() != 0 || !string.IsNullOrWhiteSpace(gameName);

        return new FriendSnapshot(
            id,
            _friends.GetFriendPersonaName(id) ?? string.Empty,
            inGame ? FriendPresence.InGame : MapPresence(state),
            ToAvatarHash(_friends.GetFriendAvatar(id)),
            string.IsNullOrWhiteSpace(gameName) ? null : gameName);
    }

    private static FriendPresence MapPresence(EPersonaState state) => state switch
    {
        EPersonaState.Online => FriendPresence.Online,
        EPersonaState.Busy => FriendPresence.Busy,
        EPersonaState.Away => FriendPresence.Away,
        EPersonaState.Snooze => FriendPresence.Snooze,
        EPersonaState.LookingToTrade => FriendPresence.LookingToTrade,
        EPersonaState.LookingToPlay => FriendPresence.LookingToPlay,
        _ => FriendPresence.Offline
    };

    private static string? ToAvatarHash(byte[]? hash)
    {
        if (hash == null || hash.Length == 0 || hash.All(b => b == 0))
            return null;

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void OnLicenseList(SteamApps.LicenseListCallback callback)
    {
        if (callback.LicenseList.Count == 0)
            return;

        _ = Task.Run(() => BuildLibraryAsync(callback.LicenseList));
    }

    private async Task BuildLibraryAsync(IReadOnlyList<SteamApps.LicenseListCallback.License> licenses)
    {
        if (Interlocked.CompareExchange(ref _libraryBusy, 1, 0) != 0)
            return;

        try
        {
            var appIds = new HashSet<uint>();

            foreach (var chunk in licenses.Chunk(50))
            {
                var result = await _apps.PICSGetProductInfo(
                    [],
                    chunk.Select(l => new SteamApps.PICSRequest(l.PackageID, l.AccessToken)));

                if (result.Results == null)
                    continue;

                foreach (var package in result.Results.SelectMany(r => r.Packages.Values))
                {
                    foreach (var child in package.KeyValues["appids"].Children)
                    {
                        if (uint.TryParse(child.Value, out var appId) && appId != 0)
                            appIds.Add(appId);
                    }
                }
            }

            var snapshots = new List<AppSnapshot>();

            foreach (var chunk in appIds.Chunk(60))
            {
                var result = await _apps.PICSGetProductInfo(
                    chunk.Select(id => new SteamApps.PICSRequest(id)),
                    []);

                if (result.Results == null)
                    continue;

                foreach (var app in result.Results.SelectMany(r => r.Apps.Values))
                {
                    var name = app.KeyValues["common"]["name"].AsString();

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    var type = app.KeyValues["common"]["type"].AsString() ?? string.Empty;
                    snapshots.Add(new AppSnapshot(app.ID, name, type.ToLowerInvariant()));
                }
            }

            if (State == SessionState.Disconnected)
                return;

            LibraryLoaded?.Invoke(snapshots
                .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray());
        }
        catch
        {
        }
        finally
        {
            Interlocked.Exchange(ref _libraryBusy, 0);
        }
    }

    private void OnDisconnected(SteamClient.DisconnectedCallback callback)
    {
        _friendIds.Clear();

        if (State == SessionState.Online)
        {
            SetState(SessionState.Disconnected);
            LoggedOut?.Invoke();
            return;
        }

        _connected.TrySetException(new HttpRequestException("Connection was closed before logging on."));
        SetState(SessionState.Disconnected);
    }

    private void SetState(SessionState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    private static LogonFailureReason Map(EResult result) => result switch
    {
        EResult.InvalidPassword => LogonFailureReason.BadCredentials,
        EResult.AccountLoginDeniedNeedTwoFactor => LogonFailureReason.BadGuardCode,
        EResult.TwoFactorCodeMismatch => LogonFailureReason.BadGuardCode,
        EResult.AccountLogonDenied => LogonFailureReason.BadGuardCode,
        EResult.RateLimitExceeded => LogonFailureReason.RateLimited,
        EResult.Timeout => LogonFailureReason.Timeout,
        _ => LogonFailureReason.Other
    };

    private async Task PumpLoop()
    {
        try
        {
            while (!_pumpCts.IsCancellationRequested)
            {
                _manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(100));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static TaskCompletionSource NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

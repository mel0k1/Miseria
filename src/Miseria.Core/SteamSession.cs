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
    private readonly CancellationTokenSource _pumpCts = new();

    private TaskCompletionSource _connected = NewCompletion();
    private string? _guardData;
    private string? _accountName;

    public event Action<SessionState>? StateChanged;
    public event Action<GuardPrompt, TaskCompletionSource<string?>>? GuardRequested;
    public event Action<AccountSnapshot>? LoggedIn;
    public event Action<LogonFailure>? LogonFailed;
    public event Action? LoggedOut;

    public SessionState State { get; private set; } = SessionState.Disconnected;

    public SteamSession()
    {
        _client = new SteamClient();
        _manager = new CallbackManager(_client);
        _user = _client.GetHandler<SteamUser>()!;

        _manager.Subscribe<SteamClient.ConnectedCallback>(_ => _connected.TrySetResult());
        _manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
        _manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);

        _ = Task.Run(PumpLoop);
    }

    public async Task LogOnAsync(LogonRequest request, CancellationToken cancellationToken = default)
    {
        if (State == SessionState.Online)
            throw new InvalidOperationException("Session is already online.");

        SetState(SessionState.Connecting);

        try
        {
            if (!_client.IsConnected)
            {
                _connected = NewCompletion();
                _client.Connect();

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                await _connected.Task.WaitAsync(timeout.Token);
            }

            SetState(SessionState.Authenticating);

            var authSession = await _client.Authentication.BeginAuthSessionViaCredentialsAsync(new AuthSessionDetails
            {
                Username = request.Username,
                Password = request.Password,
                IsPersistentSession = false,
                GuardData = _guardData,
                Authenticator = new InteractiveAuthenticator(AskGuard)
            });

            var poll = await authSession.PollingWaitForResultAsync(cancellationToken);

            _accountName = poll.AccountName;

            if (poll.NewGuardData != null)
                _guardData = poll.NewGuardData;

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

    public void LogOff()
    {
        if (State != SessionState.Online)
            return;

        _user.LogOff();
    }

    public void Dispose()
    {
        _pumpCts.Cancel();
        _client.Disconnect();
        _pumpCts.Dispose();
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
            SetState(SessionState.Disconnected);
            LogonFailed?.Invoke(new LogonFailure(Map(callback.Result), callback.Result.ToString()));
            return;
        }

        SetState(SessionState.Online);
        LoggedIn?.Invoke(new AccountSnapshot(
            _accountName ?? string.Empty,
            callback.ClientSteamID?.ConvertToUInt64() ?? 0,
            callback.IPCountryCode));
    }

    private void OnDisconnected(SteamClient.DisconnectedCallback callback)
    {
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

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class LogonViewModel : ObservableObject
{
    private readonly SteamSession _session;
    private TaskCompletionSource<string?>? _guardCompletion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LogOnCommand))]
    private string _username = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LogOnCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LogOnCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isGuardRequested;

    [ObservableProperty]
    private string _guardMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitGuardCommand))]
    private string _guardCode = string.Empty;

    public LogonViewModel(SteamSession session)
    {
        _session = session;
        _session.StateChanged += OnStateChanged;
        _session.GuardRequested += OnGuardRequested;
        _session.LogonFailed += OnLogonFailed;
    }

    private bool CanLogOn() =>
        !IsBusy && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);

    private bool CanSubmitGuard() => !string.IsNullOrWhiteSpace(GuardCode);

    public async Task BeginStoredLogon(StoredSession session)
    {
        ErrorMessage = null;
        IsBusy = true;
        StatusText = "Выполняется автоматический вход";
        try
        {
            await _session.LogOnWithTokenAsync(session);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanLogOn))]
    private async Task LogOnAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            await _session.LogOnAsync(new LogonRequest(Username.Trim(), Password), cancellationToken);
        }
        finally
        {
            IsBusy = false;
            Password = string.Empty;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSubmitGuard))]
    private void SubmitGuard()
    {
        var code = GuardCode.Trim();
        IsGuardRequested = false;
        GuardCode = string.Empty;
        ErrorMessage = null;
        StatusText = "Проверка кода";
        _guardCompletion?.TrySetResult(code);
    }

    [RelayCommand]
    private void CancelGuard()
    {
        IsGuardRequested = false;
        GuardCode = string.Empty;
        _guardCompletion?.TrySetResult(null);
    }

    private void OnStateChanged(SessionState state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            StatusText = state switch
            {
                SessionState.Connecting => "Подключение к Steam",
                SessionState.Authenticating => "Аутентификация",
                SessionState.Online => "Вход выполнен",
                _ => string.Empty
            };
        });
    }

    private void OnGuardRequested(GuardPrompt prompt, TaskCompletionSource<string?> completion)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _guardCompletion = completion;
            var hint = prompt.Kind == GuardKind.EmailCode
                ? $"Код Steam Guard отправлен на {prompt.Email}"
                : "Введите код из приложения Steam Guard на вашем телефоне";
            GuardMessage = prompt.PreviousCodeWasIncorrect
                ? $"{hint}. Предыдущий код не принят, попробуйте снова."
                : hint;
            IsGuardRequested = true;
        });
    }

    private void OnLogonFailed(LogonFailure failure)
    {
        Dispatcher.UIThread.Post(() =>
        {
            StatusText = string.Empty;
            ErrorMessage = failure.Reason switch
            {
                LogonFailureReason.BadCredentials => "Неверный логин или пароль",
                LogonFailureReason.BadGuardCode => "Неверный код Steam Guard",
                LogonFailureReason.InvalidSession => "Сохранённая сессия недействительна, войдите заново",
                LogonFailureReason.RateLimited => "Слишком много попыток входа. Повторите позже",
                LogonFailureReason.ConnectionFailed => "Не удалось подключиться к Steam",
                LogonFailureReason.Timeout => "Steam не ответил вовремя",
                LogonFailureReason.Cancelled => string.Empty,
                _ => $"Ошибка входа: {failure.Detail}"
            };
        });
    }
}

using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class HomeViewModel : ObservableObject
{
    private readonly SteamSession _session;

    [ObservableProperty]
    private AccountSnapshot? _account;

    [ObservableProperty]
    private bool _isConnected;

    public HomeViewModel(SteamSession session)
    {
        _session = session;
        _session.LoggedOut += () => Dispatcher.UIThread.Post(() => IsConnected = false);
    }

    public void Initialize(AccountSnapshot account)
    {
        Account = account;
        IsConnected = true;
    }

    [RelayCommand]
    private void LogOff() => _session.LogOff();
}

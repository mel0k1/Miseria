using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Miseria.App.Services;
using Miseria.App.ViewModels;
using Miseria.App.Views;
using Miseria.Core;

namespace Miseria.App;

public class App : Application
{
    private ServiceProvider? _services;
    private LogonWindow? _logonWindow;
    private MainWindow? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var collection = new ServiceCollection();

        collection.AddSingleton<SettingsStore>();
        collection.AddSingleton<SessionStore>();
        collection.AddSingleton<SteamSession>();
        collection.AddSingleton<AvatarCache>();
        collection.AddSingleton<AppIconCache>();
        collection.AddSingleton<ThemeService>();
        collection.AddSingleton<LogonViewModel>();
        collection.AddSingleton<MainViewModel>();
        collection.AddSingleton<FriendsViewModel>();
        collection.AddSingleton<HomeViewModel>();
        collection.AddSingleton<LibraryViewModel>();
        collection.AddSingleton<DownloadViewModel>();
        collection.AddSingleton<SettingsViewModel>();

        _services = collection.BuildServiceProvider();

        var session = _services.GetRequiredService<SteamSession>();
        session.LoggedIn += account => Dispatcher.UIThread.Post(() => ShowMain(account));
        session.LoggedOut += () => Dispatcher.UIThread.Post(ShowLogon);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            _services.GetRequiredService<ThemeService>().ApplySavedTheme();
            ShowLogon();

            var stored = _services.GetRequiredService<SessionStore>().TryLoad();
            if (stored != null)
                _ = _services.GetRequiredService<LogonViewModel>().BeginStoredLogon(stored);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowMain(AccountSnapshot account)
    {
        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow(_services!.GetRequiredService<MainViewModel>());
            _mainWindow.Show();
        }

        _mainWindow.ViewModel.Initialize(account);
        _logonWindow?.Close();
        _logonWindow = null;
    }

    private void ShowLogon()
    {
        if (_logonWindow == null)
        {
            _logonWindow = new LogonWindow(_services!.GetRequiredService<LogonViewModel>());
            _logonWindow.Show();
        }

        _mainWindow?.Close();
        _mainWindow = null;
    }
}

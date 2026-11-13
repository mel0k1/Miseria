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
    public static bool ScreenshotMode { get; set; }

    private ServiceProvider? _services;
    private LogonWindow? _logonWindow;
    private MainWindow? _mainWindow;
    private AccountSnapshot? _lastAccount;
    private bool _familyViewHandled;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var collection = new ServiceCollection();

        collection.AddSingleton<SettingsStore>();
        collection.AddSingleton<InstallStore>();
        collection.AddSingleton<ManifestStore>();
        collection.AddSingleton<SessionStore>();
        collection.AddSingleton<SteamSession>();
        collection.AddSingleton<SteamWebApi>(sp => new SteamWebApi(() =>
            sp.GetRequiredService<SteamSession>().GetWebIdentityAsync()));
        collection.AddSingleton<AvatarCache>();
        collection.AddSingleton<AppIconCache>();
        collection.AddSingleton<ItemIconCache>();
        collection.AddSingleton<ThemeService>();
        collection.AddSingleton<MarketService>();
        collection.AddSingleton(sp => new InventoryService(
            sp.GetRequiredService<SteamWebApi>(),
            () => Task.FromResult(sp.GetRequiredService<SteamSession>().OwnSteamId)));
        collection.AddSingleton<TradeService>();
        collection.AddSingleton<WorkshopService>();
        collection.AddSingleton<GroupService>();
        collection.AddSingleton<ActivityService>();
        collection.AddSingleton<CommunityService>();
        collection.AddSingleton<ProfileService>();
        collection.AddSingleton<LogonViewModel>();
        collection.AddSingleton<MainViewModel>();
        collection.AddSingleton<FriendsViewModel>();
        collection.AddSingleton<FeedViewModel>();
        collection.AddSingleton<HomeViewModel>();
        collection.AddSingleton<LibraryViewModel>();
        collection.AddSingleton<AchievementsViewModel>();
        collection.AddSingleton<DownloadViewModel>();
        collection.AddSingleton<MarketViewModel>();
        collection.AddSingleton<InventoryViewModel>();
        collection.AddSingleton<TradesViewModel>();
        collection.AddSingleton<WorkshopViewModel>();
        collection.AddSingleton<GroupsViewModel>();
        collection.AddSingleton<CommunityViewModel>();
        collection.AddSingleton<ProfileViewModel>();
        collection.AddSingleton<SettingsViewModel>();

        _services = collection.BuildServiceProvider();

        var session = _services.GetRequiredService<SteamSession>();
        session.LoggedIn += account => Dispatcher.UIThread.Post(() => OnLoggedIn(account));
        session.LoggedOut += () => Dispatcher.UIThread.Post(ShowLogon);

        _services.GetRequiredService<LogonViewModel>().ProceedRequested += OnFamilyViewProceed;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime && !ScreenshotMode)
        {
            _services.GetRequiredService<ThemeService>().ApplySavedTheme();
            ShowLogon();

            var stored = _services.GetRequiredService<SessionStore>().TryLoad();
            if (stored != null)
                _ = _services.GetRequiredService<LogonViewModel>().BeginStoredLogon(stored);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnLoggedIn(AccountSnapshot account)
    {
        _lastAccount = account;

        if (account.FamilyViewLocked && !_familyViewHandled)
        {
            _services!.GetRequiredService<LogonViewModel>().RequestFamilyView();
            return;
        }

        ShowMain(account);
    }

    private void OnFamilyViewProceed()
    {
        _familyViewHandled = true;

        if (_lastAccount != null)
            ShowMain(_lastAccount);
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
        _familyViewHandled = false;
        _lastAccount = null;

        if (_logonWindow == null)
        {
            _logonWindow = new LogonWindow(_services!.GetRequiredService<LogonViewModel>());
            _logonWindow.Show();
        }

        _mainWindow?.Close();
        _mainWindow = null;
    }
}

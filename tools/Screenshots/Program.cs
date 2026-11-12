using System.Globalization;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Metadata;
using Avalonia.Threading;
using Miseria.App;
using Miseria.App.Services;
using Miseria.App.Theming;
using Miseria.App.ViewModels;
using Miseria.App.Views;
using Miseria.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Screenshots;

internal static class Program
{
    private static readonly (uint AppId, string Name)[] LibraryApps =
    [
        (730, "Counter-Strike 2"),
        (570, "Dota 2"),
        (292030, "The Witcher 3: Wild Hunt"),
        (1245620, "ELDEN RING"),
        (1091500, "Cyberpunk 2077"),
        (548430, "Deep Rock Galactic"),
        (1145360, "Hades"),
        (413150, "Stardew Valley"),
        (367520, "Hollow Knight"),
        (632470, "Disco Elysium"),
        (504230, "Celeste"),
        (588650, "Dead Cells"),
        (105600, "Terraria"),
        (646570, "Slay the Spire"),
        (427520, "Factorio"),
        (294100, "RimWorld"),
        (264710, "Subnautica"),
        (227300, "Euro Truck Simulator 2"),
        (220, "Half-Life 2"),
        (620, "Portal 2"),
    ];

    private static readonly (string Name, FriendPresence Presence, string? Game)[] Friends =
    [
        ("Dmitry", FriendPresence.InGame, "Counter-Strike 2"),
        ("Anna", FriendPresence.Online, null),
        ("PixelPioneer", FriendPresence.InGame, "Dota 2"),
        ("wolflord", FriendPresence.LookingToTrade, null),
        ("Sasha", FriendPresence.Online, null),
        ("Nikita", FriendPresence.Away, null),
        ("Roman", FriendPresence.LookingToPlay, null),
        ("Kolya", FriendPresence.Snooze, null),
        ("Masha", FriendPresence.Offline, null),
        ("Vladimir", FriendPresence.Offline, null),
    ];

    private static readonly Color[] AvatarColors =
    [
        Color.Parse("#4C6EF5"),
        Color.Parse("#12B886"),
        Color.Parse("#E8590C"),
        Color.Parse("#9C36B5"),
        Color.Parse("#228BE6"),
        Color.Parse("#F59F00"),
        Color.Parse("#C2255C"),
        Color.Parse("#2F9E44"),
        Color.Parse("#7048E8"),
        Color.Parse("#0CA678")
    ];

    public static int Main(string[] args)
    {
        var outDir = Path.GetFullPath(args.Length > 0 ? args[0] : "docs/screenshots");
        Directory.CreateDirectory(outDir);

        var sandbox = Path.Combine(Path.GetTempPath(), "miseria-shots");
        Directory.CreateDirectory(Path.Combine(sandbox, "config"));
        Directory.CreateDirectory(Path.Combine(sandbox, "data"));
        Directory.CreateDirectory(Path.Combine(sandbox, "capsules"));
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Combine(sandbox, "config"));
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(sandbox, "data"));

        App.ScreenshotMode = true;

        var exitCode = 2;
        var builder = AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
        builder.AfterSetup(_ => Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await RunAsync(outDir);
                exitCode = 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
            finally
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
                    lifetime.Shutdown();
            }
        }, DispatcherPriority.ApplicationIdle));

        builder.StartWithClassicDesktopLifetime([], ShutdownMode.OnExplicitShutdown);
        return exitCode;
    }

    private static async Task RunAsync(string outDir)
    {
        var services = new ServiceCollection()
            .AddSingleton<SettingsStore>()
            .AddSingleton<InstallStore>()
            .AddSingleton<ManifestStore>()
            .AddSingleton<SessionStore>()
            .AddSingleton<SteamSession>()
            .AddSingleton<SteamWebApi>(sp => new SteamWebApi(() =>
                sp.GetRequiredService<SteamSession>().GetWebIdentityAsync()))
            .AddSingleton<AvatarCache>()
            .AddSingleton<AppIconCache>()
            .AddSingleton<ItemIconCache>()
            .AddSingleton<ThemeService>()
            .AddSingleton<MarketService>()
            .AddSingleton<TradeService>()
            .AddSingleton<CommunityService>()
            .AddSingleton<LogonViewModel>()
            .AddSingleton<MainViewModel>()
            .AddSingleton<FriendsViewModel>()
            .AddSingleton<HomeViewModel>()
            .AddSingleton<LibraryViewModel>()
            .AddSingleton<DownloadViewModel>()
            .AddSingleton<MarketViewModel>()
            .AddSingleton<TradesViewModel>()
            .AddSingleton<CommunityViewModel>()
            .AddSingleton<ProfileViewModel>()
            .AddSingleton<SettingsViewModel>()
            .BuildServiceProvider();

        var theme = services.GetRequiredService<ThemeService>();
        var logon = services.GetRequiredService<LogonViewModel>();
        var main = services.GetRequiredService<MainViewModel>();
        var library = main.Library;
        var friends = main.Friends;
        var download = main.Download;
        var home = main.Home;

        var capsules = await DownloadCapsulesAsync(Path.Combine(Path.GetTempPath(), "miseria-shots", "capsules"));

        library.ReplaceApps(LibraryApps.Select(a => new AppSnapshot(a.AppId, a.Name, "game", null)).ToArray());

        foreach (var item in library.Apps)
        {
            if (capsules.TryGetValue(item.AppId, out var file))
            {
                item.Capsule = new Bitmap(file);
                item.HasCapsule = true;
            }

            item.IsInstalled = item.AppId is 730 or 570 or 413150;
        }

        friends.ReplaceFriends(Enumerable.Range(0, Friends.Length).Select(i => 76561198000000000UL + (ulong)i).ToArray());

        for (var i = 0; i < friends.Friends.Count && i < Friends.Length; i++)
        {
            var item = friends.Friends[i];
            var (name, presence, game) = Friends[i];
            item.UpdateFrom(new FriendSnapshot(item.SteamId, name, presence, null, game));
            item.Avatar = CreateAvatar(name, i);
            item.HasAvatar = true;
        }

        download.HasInstalls = true;
        download.UpdatesStatus = "Проверено игр: 3";
        download.Updates.Add(new UpdateItem(730, "Counter-Strike 2", "Обновление доступно · 1,2 ГБ · файлов: 152", true));
        download.Updates.Add(new UpdateItem(570, "Dota 2", "Актуальная версия · ветка public", false));
        download.Updates.Add(new UpdateItem(413150, "Stardew Valley", "Актуальная версия · ветка public", false));
        download.HasContent = true;
        download.IsPicking = false;
        download.IsActive = true;
        download.IsPreparing = false;
        download.AppName = "Cyberpunk 2077";
        download.ProgressPercent = 47.3;
        download.ProgressText = "6,4 ГБ из 13,6 ГБ";
        download.SpeedText = "42,7 МБ/с";
        download.StatusText = "r6/config/settings.ini";
        download.DestinationText = "/home/user/Miseria/games/1091500";

        await BuildFakeTradesAsync(services, friends);
        await Task.Delay(12000);

        home.Initialize(new AccountSnapshot("mel0k1", 76561198012345678, "RU", false));

        var profile = main.Profile;
        profile.Initialize(new AccountSnapshot("mel0k1", 76561198012345678, "RU", false));
        profile.ApplySnapshot(new ProfileSnapshot(
            76561198012345678,
            "mel0k1",
            null,
            FriendPresence.Online,
            null,
            null,
            "Россия",
            "Люблю инди-игры и старые шутеры.",
            new DateTimeOffset(2013, 5, 17, 12, 0, 0, TimeSpan.Zero)));

        logon.Username = "mel0k1";
        logon.Password = "correct-horse";

        theme.Apply(AppTheme.Light);
        await Shot(new LogonWindow(logon), Path.Combine(outDir, "logon-light.png"));

        theme.Apply(AppTheme.Kiwi);
        await Shot(new LogonWindow(logon), Path.Combine(outDir, "logon-kiwi.png"));

        theme.Apply(AppTheme.Dark);
        var window = new MainWindow(main);
        window.Show();
        await PumpAsync();

        window.NavigateTo("friends");
        await PumpAsync();
        await CaptureAsync(window, Path.Combine(outDir, "friends-dark.png"));

        window.NavigateTo("library");
        library.IsGrid = true;
        await PumpAsync();
        await CaptureAsync(window, Path.Combine(outDir, "library-grid-dark.png"));

        library.IsGrid = false;
        await PumpAsync();
        await CaptureAsync(window, Path.Combine(outDir, "library-list-dark.png"));

        window.NavigateTo("downloads");
        await PumpAsync();
        await CaptureAsync(window, Path.Combine(outDir, "downloads-dark.png"));

        window.NavigateTo("home");
        await PumpAsync();
        await CaptureAsync(window, Path.Combine(outDir, "home-dark.png"));

        window.NavigateTo("profile");
        await PumpAsync();
        await CaptureAsync(window, Path.Combine(outDir, "profile-dark.png"));

        window.NavigateTo("market");
        await WaitForMarketAsync(main.Market);

        if (main.Market.Results.Count > 0)
        {
            main.Market.SelectedItem = main.Market.Results[0];

            for (var attempt = 0; attempt < 3 && !main.Market.HasDetail; attempt++)
            {
                await Task.Delay(2000);
                main.Market.SelectedItem = null;
                main.Market.SelectedItem = main.Market.Results[0];
            }
        }

        await Task.Delay(1500);
        await CaptureAsync(window, Path.Combine(outDir, "market-dark.png"));

        window.NavigateTo("trades");
        await Task.Delay(2200);
        await CaptureAsync(window, Path.Combine(outDir, "trades-dark.png"));

        window.NavigateTo("community");
        await Task.Delay(5000);
        await CaptureAsync(window, Path.Combine(outDir, "community-dark.png"));

        theme.Apply(AppTheme.Kiwi);
        window.NavigateTo("friends");
        await PumpAsync();
        await CaptureAsync(window, Path.Combine(outDir, "friends-kiwi.png"));

        theme.Apply(AppTheme.Dark);
        window.NavigateTo("friends");
        friends.OpenChat(friends.Friends[0]);
        friends.Messages.Add(new ChatBubble("Ну что, готов к катке?", new DateTimeOffset(2026, 1, 17, 19, 41, 0, TimeSpan.Zero), false));
        friends.Messages.Add(new ChatBubble("Ща зайду, минуточку", new DateTimeOffset(2026, 1, 17, 19, 42, 0, TimeSpan.Zero), true));
        friends.Messages.Add(new ChatBubble("Го новую карту", new DateTimeOffset(2026, 1, 17, 19, 42, 30, TimeSpan.Zero), false));
        friends.Messages.Add(new ChatBubble("Давай, только премьер", new DateTimeOffset(2026, 1, 17, 19, 43, 0, TimeSpan.Zero), true));
        await PumpAsync();
        await CaptureAsync(window, Path.Combine(outDir, "chat-dark.png"));

        window.Close();
    }

    private static async Task WaitForMarketAsync(MarketViewModel market)
    {
        for (var attempt = 0; attempt < 4 && market.Results.Count == 0; attempt++)
        {
            await Task.Delay(4000);

            if (market.Results.Count > 0)
                return;

            await market.SearchCommand.ExecuteAsync(null);
        }
    }

    private static async Task BuildFakeTradesAsync(IServiceProvider services, FriendsViewModel friends)
    {
        var trades = services.GetRequiredService<TradesViewModel>();
        var marketService = services.GetRequiredService<MarketService>();
        var icons = services.GetRequiredService<ItemIconCache>();

        trades.SuppressAutoLoad();

        var items = new List<MarketItemSnapshot>();

        for (var attempt = 0; attempt < 3 && items.Count == 0; attempt++)
        {
            try
            {
                items.AddRange((await marketService.SearchAsync(730, string.Empty, 0, 10)).Items);
            }
            catch
            {
                await Task.Delay(2500);
            }
        }

        var descriptions = new Dictionary<(uint AppId, long ClassId, long InstanceId), TradeDescriptionSnapshot>();

        for (var i = 0; i < items.Count; i++)
        {
            descriptions[(730, i + 1, 0)] = new TradeDescriptionSnapshot(
                730,
                i + 1,
                0,
                items[i].Name,
                i % 2 == 0 ? "Контейнер" : "Обычный предмет",
                items[i].IconUrl,
                null);
        }

        ulong PartnerId(int index) => 76561198000000000UL + (ulong)index;

        TradeOfferSnapshot Snapshot(int index, TradeDirection direction, ulong partner, string message, bool mobile, int giveFrom, int receiveFrom)
        {
            TradeAssetSnapshot Asset(int slot) => new(730, slot + 1, 0, 1000 + slot);

            return new TradeOfferSnapshot(
                3314000000UL + (ulong)index,
                partner,
                direction == TradeDirection.Sent,
                direction,
                message,
                DateTimeOffset.UtcNow.AddHours(-2 - index),
                mobile,
                [Asset(giveFrom), Asset(giveFrom + 1)],
                [Asset(receiveFrom), Asset(receiveFrom + 1)]);
        }

        var first = new TradeOfferItem(
            Snapshot(0, TradeDirection.Incoming, PartnerId(0), "Го обмен, у меня лишние кейсы после турнира", true, 0, 2),
            icons,
            descriptions);
        first.SetPartner(friends.Friends.FirstOrDefault(f => f.SteamId == PartnerId(0)));

        var second = new TradeOfferItem(
            Snapshot(1, TradeDirection.Incoming, PartnerId(3), string.Empty, false, 4, 6),
            icons,
            descriptions);
        second.SetPartner(friends.Friends.FirstOrDefault(f => f.SteamId == PartnerId(3)));

        trades.Offers.Add(first);
        trades.Offers.Add(second);
        trades.StatusText = "Активных обменов: 2";
    }

    private static async Task Shot(Window window, string path)
    {
        window.Show();
        await PumpAsync();
        await CaptureAsync(window, path);
        window.Close();
    }

    private static async Task PumpAsync()
    {
        for (var i = 0; i < 12; i++)
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

        await Task.Delay(200);
    }

    private static async Task CaptureAsync(Window window, string path)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var scale = window.RenderScaling;
            using var bitmap = new RenderTargetBitmap(
                new PixelSize(
                    (int)Math.Ceiling(window.Bounds.Width * scale),
                    (int)Math.Ceiling(window.Bounds.Height * scale)),
                new Vector(96, 96) * scale);

            bitmap.Render(window);
            bitmap.Save(new FileStream(path, FileMode.Create), new PngBitmapEncoderOptions());
        });

        await Task.Delay(30);
    }

    private static Bitmap CreateAvatar(string name, int index)
    {
        const int size = 44;
        var brush = new SolidColorBrush(AvatarColors[index % AvatarColors.Length]);
        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));

        using (var context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(brush, new Rect(0, 0, size, size), 6f);
            var initial = name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?";
            var text = new FormattedText(
                initial,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold),
                20,
                new SolidColorBrush(Colors.White));
            var point = new Point(
                (size - text.Width) / 2,
                (size - text.Height) / 2);
            context.DrawText(text, point);
        }

        return bitmap;
    }

    private static async Task<Dictionary<uint, string>> DownloadCapsulesAsync(string directory)
    {
        Directory.CreateDirectory(directory);

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var map = new Dictionary<uint, string>();

        foreach (var (appId, _) in LibraryApps)
        {
            var file = Path.Combine(directory, appId + ".jpg");

            if (!File.Exists(file))
            {
                try
                {
                    var bytes = await http.GetByteArrayAsync(
                        $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/capsule_231x87.jpg");
                    await File.WriteAllBytesAsync(file, bytes);
                }
                catch
                {
                    continue;
                }
            }

            if (File.Exists(file))
                map[appId] = file;
        }

        return map;
    }
}

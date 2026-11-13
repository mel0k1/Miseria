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
            .AddSingleton(sp => new InventoryService(
                sp.GetRequiredService<SteamWebApi>(),
                () => Task.FromResult(76561198012345678UL)))
            .AddSingleton<TradeService>()
            .AddSingleton<WorkshopService>()
            .AddSingleton<GroupService>()
            .AddSingleton<ActivityService>()
            .AddSingleton<CommunityService>()
            .AddSingleton<ProfileService>()
            .AddSingleton<LogonViewModel>()
            .AddSingleton<MainViewModel>()
            .AddSingleton<FriendsViewModel>()
            .AddSingleton<FeedViewModel>()
            .AddSingleton<HomeViewModel>()
            .AddSingleton<LibraryViewModel>()
            .AddSingleton<DownloadViewModel>()
            .AddSingleton<MarketViewModel>()
            .AddSingleton<InventoryViewModel>()
            .AddSingleton<TradesViewModel>()
            .AddSingleton<WorkshopViewModel>()
            .AddSingleton<GroupsViewModel>()
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

        await main.Feed.EnsureLoadedAsync();

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

        profile.Level = 42;
        profile.GamesCount = 214;
        profile.TotalHours = 9316.4;
        profile.TopGames.Add(new ProfileGameRow("Counter-Strike 2", "2 413 ч."));
        profile.TopGames.Add(new ProfileGameRow("Terraria", "318,5 ч."));
        profile.TopGames.Add(new ProfileGameRow("Stardew Valley", "147 ч."));
        profile.TopGames.Add(new ProfileGameRow("Hollow Knight", "86 ч."));

        for (var i = 0; i < 6; i++)
        {
            var row = new ProfileScreenshotRow(
                string.Empty,
                "https://steamcommunity.com",
                services.GetRequiredService<ItemIconCache>());
            row.SetImage(CreateScreenshot(i));
            profile.Screenshots.Add(row);
        }

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

        if (main.Market.Results.Count > 0)
        {
            main.Market.StartBuyOrderCommand.Execute(null);
            await Task.Delay(4000);
            main.Market.OrderPriceInput = "24.99";
            main.Market.OrderQuantityInput = "2";
            await PumpAsync();
            await CaptureAsync(window, Path.Combine(outDir, "market-buyorder-dark.png"));
            main.Market.CancelOrderPanelCommand.Execute(null);
            await PumpAsync();
        }

        window.NavigateTo("trades");
        await Task.Delay(2200);
        await CaptureAsync(window, Path.Combine(outDir, "trades-dark.png"));

        await SeedTradeCreationAsync(main, services);
        await PumpAsync();
        await CaptureAsync(window, Path.Combine(outDir, "trades-create-dark.png"));
        main.Trades.IsCreating = false;

        window.NavigateTo("inventory");
        await SeedInventoryAsync(main.Inventory, services);
        await Task.Delay(2500);
        await PumpAsync();
        await CaptureAsync(window, Path.Combine(outDir, "inventory-dark.png"));

        window.NavigateTo("workshop");
        await SeedWorkshopAsync(main.Workshop, services);
        await Task.Delay(3500);
        await PumpAsync();
        await CaptureAsync(window, Path.Combine(outDir, "workshop-dark.png"));

        window.NavigateTo("groups");
        await SeedGroupsAsync(main.Groups, services);
        await Task.Delay(2500);
        await PumpAsync();
        await CaptureAsync(window, Path.Combine(outDir, "groups-dark.png"));

        await SeedGroupFeedAsync(main.Groups);
        main.Groups.Section = 1;
        await PumpAsync();
        await CaptureAsync(window, Path.Combine(outDir, "groups-announcements-dark.png"));
        main.Groups.Section = 2;
        await PumpAsync();
        await CaptureAsync(window, Path.Combine(outDir, "groups-discussions-dark.png"));
        main.Groups.Section = 0;
        await PumpAsync();

        window.NavigateTo("community");
        await Task.Delay(5000);
        await CaptureAsync(window, Path.Combine(outDir, "community-dark.png"));

        window.NavigateTo("feed");
        await PumpAsync();
        await SeedFeedAsync(main.Feed);
        await PumpAsync();
        await CaptureAsync(window, Path.Combine(outDir, "feed-dark.png"));

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
            TradeAssetSnapshot Asset(int slot) => new(730, 2, slot + 1, 0, 1000 + slot);

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

    private static async Task SeedTradeCreationAsync(MainViewModel main, IServiceProvider services)
    {
        var trades = main.Trades;
        var icons = services.GetRequiredService<ItemIconCache>();
        var market = services.GetRequiredService<MarketService>();

        trades.SuppressAutoLoad();
        trades.IsCreating = true;
        trades.OfferMessage = "Меняю кейсы на ключи, го";
        trades.SelectedFriend = main.Friends.Friends[3];
        await Task.Delay(2500);

        var items = await LoadMarketItemsAsync(market);

        for (var i = 0; i < items.Count; i++)
        {
            var snapshot = new InventoryItemSnapshot(
                730,
                2,
                (1000 + i).ToString(CultureInfo.InvariantCulture),
                i + 1,
                0,
                items[i].Name,
                items[i].HashName,
                i % 2 == 0 ? "Контейнер" : "Обычный предмет",
                items[i].IconUrl,
                null,
                true,
                true,
                string.Empty);
            var item = new InventoryItem(snapshot);
            trades.AddOwnTradeItem(item);
            _ = LoadItemIconAsync(item, icons, items[i].IconUrl);
        }

        if (trades.OwnItems.Count > 0)
            trades.OwnItems[0].IsSelected = true;

        if (trades.OwnItems.Count > 2)
            trades.OwnItems[2].IsSelected = true;

        trades.TheirStatusText = "Инвентарь друга пуст или закрыт настройками приватности";
    }

    private static async Task SeedInventoryAsync(InventoryViewModel inventory, IServiceProvider services)
    {
        var icons = services.GetRequiredService<ItemIconCache>();
        var market = services.GetRequiredService<MarketService>();

        await inventory.EnsureLoadedAsync();

        var items = await LoadMarketItemsAsync(market);
        inventory.SelectedItem = null;
        inventory.Items.Clear();
        inventory.VisibleItems.Clear();

        for (var i = 0; i < items.Count; i++)
        {
            var snapshot = new InventoryItemSnapshot(
                730,
                2,
                (2000 + i).ToString(CultureInfo.InvariantCulture),
                i + 1,
                0,
                items[i].Name,
                items[i].HashName,
                i % 2 == 0 ? "Контейнер" : "Обычный предмет",
                items[i].IconUrl,
                null,
                true,
                true,
                i % 2 == 0 ? "Открывается, чтобы получить один из предметов внутри." : string.Empty);
            var item = new InventoryItem(snapshot);
            inventory.Items.Add(item);
            inventory.VisibleItems.Add(item);
            _ = LoadItemIconAsync(item, icons, items[i].IconUrl);
        }

        inventory.StatusText = $"Предметов: {inventory.Items.Count} из {inventory.Items.Count}";

        if (inventory.VisibleItems.Count > 0)
        {
            inventory.SelectedItem = inventory.VisibleItems[0];
            inventory.SellPrice = "49,99";
        }
    }

    private static async Task SeedWorkshopAsync(WorkshopViewModel workshop, IServiceProvider services)
    {
        var icons = services.GetRequiredService<ItemIconCache>();
        var market = services.GetRequiredService<MarketService>();

        await workshop.EnsureLoadedAsync();

        var items = await LoadMarketItemsAsync(market);
        var titles = new[]
        {
            "Пыль 2 — ремейк классической карты",
            "Зомби-кампания: Последний рубеж",
            "Мод на графику: Панорама",
            "Кампания: Тёмные пещеры",
            "Арена для дуэлей",
            "Тренировочная карта для прицела",
            "Сборник скинов оружий",
            "Модели игроков: Легион"
        };

        workshop.SelectedItem = null;
        workshop.Items.Clear();

        for (var i = 0; i < Math.Min(titles.Length, items.Count); i++)
        {
            var snapshot = new WorkshopFileSnapshot(
                (2900000000 + i).ToString(CultureInfo.InvariantCulture),
                730,
                titles[i],
                "Карты, моды и кампании от сообщества: полноценный материал с превью, тегами и статистикой загрузок.",
                $"https://community.cloudflare.steamstatic.com/economy/image/{items[i].IconUrl}/256fx144f",
                (i + 1) * 1_400_000,
                76561198000000000UL.ToString(CultureInfo.InvariantCulture),
                DateTimeOffset.UtcNow.AddDays(-40 - i * 9),
                DateTimeOffset.UtcNow.AddDays(-3 - i),
                180_000 - i * 12_000,
                24_000 - i * 900,
                900_000 - i * 45_000,
                i % 2 == 0 ? "Карты, Аркада" : "Моды, Геймплей",
                i % 2 == 0 ? "Dmitry" : "PixelPioneer");
            var item = new WorkshopItem(snapshot);
            workshop.Items.Add(item);
            _ = icons.LoadUrlAsync(snapshot.PreviewUrl).ContinueWith(t => item.SetIcon(t.Result), TaskScheduler.FromCurrentSynchronizationContext());
        }

        workshop.StatusText = "Найдено материалов: 1462";

        if (workshop.Items.Count > 0)
        {
            workshop.SelectedItem = workshop.Items[0];
            workshop.ActionMessage = string.Empty;
        }
    }

    private static async Task SeedFeedAsync(FeedViewModel feed)
    {
        var entries = new[]
        {
            ("Dmitry", "добавил Counter-Strike 2 в свои избранные игры", "12 минут назад"),
            ("Anna", "сделала новый скриншот в Hades", "38 минут назад"),
            ("PixelPioneer", "написал обзор на Half-Life: Alyx: «Лучшая VR-игра на рынке, без вопросов»", "1 час назад"),
            ("wolflord", "начал играть в Dota 2", "2 часа назад"),
            ("Sasha", "приобрёл Deep Rock Galactic", "3 часа назад"),
            ("PixelPioneer", "успешно обменял 4 предмета с Anna", "5 часов назад"),
            ("Anna", "добавила в свою коллекцию трофей «Золото ада» в Celeste", "вчера")
        };

        feed.Entries.Clear();

        for (var i = 0; i < entries.Length; i++)
        {
            var (name, text, when) = entries[i];
            var item = new ActivityItem(new ActivitySnapshot(
                name,
                $"https://steamcommunity.com/id/{name.ToLowerInvariant()}",
                null,
                text,
                when,
                null));
            item.SetAvatar(CreateAvatar(name, i));
            feed.Entries.Add(item);
        }

        feed.StatusText = $"Записей: {feed.Entries.Count}";
    }

    private static async Task SeedGroupFeedAsync(GroupsViewModel groups)
    {
        groups.Announcements.Clear();
        groups.Announcements.Add(new AnnouncementItem(
            "Обновление Steam Client от 12 ноября",
            "https://steamcommunity.com/groups/valvesoftware/announcements/detail/1",
            "12 ноября",
            "Исправлены сбои в чате друзей, ускорена загрузка мастерской и добавлены новые фильтры в библиотеке. Полный список изменений доступен в блоге Steam."));
        groups.Announcements.Add(new AnnouncementItem(
            "Осенняя распродажа начнётся 25 ноября",
            "https://steamcommunity.com/groups/valvesoftware/announcements/detail/2",
            "8 ноября",
            "Не забудьте собрать карточки распродажи, проголосовать за любимые игры и получить уникальный профильный бейдж. Скидки коснутся более 15 тысяч приложений."));
        groups.Announcements.Add(new AnnouncementItem(
            "Обновление драйверов Steam Deck",
            "https://steamcommunity.com/groups/valvesoftware/announcements/detail/3",
            "5 ноября",
            "Выпущено обновление прошивки с улучшением производительности в актуальных играх, стабильности Wi-Fi и времени работы от батареи."));
        groups.Announcements.Add(new AnnouncementItem(
            "Итоги конкурса скриншотов",
            "https://steamcommunity.com/groups/cs2community/announcements/detail/4",
            "3 ноября",
            "Победители осеннего конкурса скриншотов определены. Лучшие работы опубликованы в витрине группы — поздравляем авторов и благодарим всех участников."));

        groups.Discussions.Clear();
        groups.Discussions.Add(new DiscussionItem(
            "Ваш топ-10 игр за 2026 год?",
            "https://steamcommunity.com/groups/valvesoftware/discussions/0/1/",
            "PixelPioneer",
            "Автор: PixelPioneer · Ответов: 42 · Последнее: 12 ноября, 18:40"));
        groups.Discussions.Add(new DiscussionItem(
            "Проблема с синхронизацией облачных сохранений",
            "https://steamcommunity.com/groups/valvesoftware/discussions/0/2/",
            "wolflord",
            "Автор: wolflord · Ответов: 7 · Последнее: 12 ноября, 16:05"));
        groups.Discussions.Add(new DiscussionItem(
            "Ищем игроков в кооператив на Deep Rock Galactic",
            "https://steamcommunity.com/groups/indiehunt/discussions/0/3/",
            "Sasha",
            "Автор: Sasha · Ответов: 15 · Последнее: 11 ноября, 21:30"));
        groups.Discussions.Add(new DiscussionItem(
            "Как вернуть классический вид библиотеки?",
            "https://steamcommunity.com/groups/cs2community/discussions/0/4/",
            "Anna",
            "Автор: Anna · Ответов: 3 · Последнее: 11 ноября, 14:12"));
        groups.Discussions.Add(new DiscussionItem(
            "Прогнозы по скидкам на распродаже",
            "https://steamcommunity.com/groups/indiehunt/discussions/0/5/",
            "Dmitry",
            "Автор: Dmitry · Ответов: 23 · Последнее: 10 ноября, 19:58"));

        groups.FeedStatusText = $"Анонсов: {groups.Announcements.Count} · Тем: {groups.Discussions.Count}";
    }

    private static async Task SeedGroupsAsync(GroupsViewModel groups, IServiceProvider services)
    {
        var icons = services.GetRequiredService<ItemIconCache>();
        var market = services.GetRequiredService<MarketService>();

        await groups.EnsureLoadedAsync();

        var items = await LoadMarketItemsAsync(market);
        var names = new[]
        {
            ("valvesoftware", "Valve Software", "Официальная группа Valve Corporation.", 482_000),
            ("cs2community", "CS2 Community", "Новости, турниры и обмен тактиками Counter-Strike 2.", 156_000),
            ("indiehunt", "Инди-охотники", "Находим жемчужины в распродажах и делимся обзорами.", 12_400)
        };

        groups.SelectedGroup = null;
        groups.Groups.Clear();

        for (var i = 0; i < names.Length; i++)
        {
            var (shortName, name, summary, members) = names[i];
            var snapshot = new GroupSnapshot(
                "10358279144000000" + i,
                shortName,
                name,
                string.Empty,
                summary,
                null,
                members,
                members / 42,
                members / 120);
            var item = new GroupItem(snapshot);
            groups.Groups.Add(item);
            _ = LoadGroupAvatarAsync(item, icons, market, items, i);
        }

        groups.StatusText = $"Групп: {groups.Groups.Count}";
        groups.ActionMessage = string.Empty;

        if (groups.Groups.Count > 0)
            groups.SelectedGroup = groups.Groups[0];
    }

    private static async Task LoadItemIconAsync(InventoryItem item, ItemIconCache icons, string? iconUrl)
    {
        var bitmap = await icons.LoadAsync(iconUrl);

        if (bitmap == null)
            return;

        await Dispatcher.UIThread.InvokeAsync(() => item.SetIcon(bitmap));
    }

    private static async Task LoadGroupAvatarAsync(GroupItem item, ItemIconCache icons, MarketService market, List<MarketItemSnapshot> items, int index)
    {
        if (items.Count == 0)
            return;

        var url = $"https://community.cloudflare.steamstatic.com/economy/image/{items[index % items.Count].IconUrl}/96fx96f";
        var bitmap = await icons.LoadUrlAsync(url);

        if (bitmap == null)
            return;

        await Dispatcher.UIThread.InvokeAsync(() => item.SetAvatar(bitmap));
    }

    private static async Task<List<MarketItemSnapshot>> LoadMarketItemsAsync(MarketService market)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var result = await market.SearchAsync(730, string.Empty, 0, 10);

                if (result.Items.Count > 0)
                    return result.Items.ToList();
            }
            catch
            {
            }

            await Task.Delay(2000);
        }

        return [];
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

    private static Bitmap CreateScreenshot(int index)
    {
        const int width = 296;
        const int height = 167;
        var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));

        using (var context = bitmap.CreateDrawingContext())
        {
            var from = AvatarColors[index % AvatarColors.Length];
            var to = AvatarColors[(index + 3) % AvatarColors.Length];
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative)
            };
            brush.GradientStops.Add(new GradientStop(from, 0));
            brush.GradientStops.Add(new GradientStop(to, 1));
            context.FillRectangle(brush, new Rect(0, 0, width, height));
        }

        return bitmap;
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

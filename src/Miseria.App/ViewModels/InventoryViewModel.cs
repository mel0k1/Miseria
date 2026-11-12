using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miseria.App.Services;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class InventoryViewModel : ObservableObject
{
    private static readonly (uint AppId, string Name)[] FallbackApps =
    [
        (730, "Counter-Strike 2"),
        (570, "Dota 2"),
        (440, "Team Fortress 2"),
        (252490, "Rust"),
        (753, "Steam")
    ];

    private readonly InventoryService _inventory;
    private readonly MarketService _market;
    private readonly ItemIconCache _icons;
    private readonly LibraryViewModel _library;
    private readonly SteamSession _session;
    private bool _loaded;
    private int _busy;

    public ObservableCollection<AppChoice> Apps { get; } = [];

    public ObservableCollection<CurrencyChoice> Currencies { get; } = [];

    public ObservableCollection<InventoryItem> Items { get; } = [];

    public ObservableCollection<InventoryItem> VisibleItems { get; } = [];

    [ObservableProperty]
    private AppChoice? _selectedApp;

    [ObservableProperty]
    private CurrencyChoice? _selectedCurrency;

    [ObservableProperty]
    private bool _onlyMarketable;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private InventoryItem? _selectedItem;

    [ObservableProperty]
    private bool _hasDetail;

    [ObservableProperty]
    private string _detailLowest = "—";

    [ObservableProperty]
    private string _detailMedian = "—";

    [ObservableProperty]
    private bool _hasPrices;

    [ObservableProperty]
    private string _sellPrice = string.Empty;

    [ObservableProperty]
    private string _sellMessage = string.Empty;

    [ObservableProperty]
    private bool _isSelling;

    public bool HasItems => VisibleItems.Count > 0;

    public bool IsEmpty => !IsBusy && !HasItems;

    public string EmptyText => "Инвентарь пуст для выбранной игры, недоступен или закрыт настройками приватности.";

    public InventoryViewModel(InventoryService inventory, MarketService market, ItemIconCache icons, LibraryViewModel library, SteamSession session)
    {
        _inventory = inventory;
        _market = market;
        _icons = icons;
        _library = library;
        _session = session;

        Currencies.Add(new CurrencyChoice(MarketCurrency.Rub, "Рубль"));
        Currencies.Add(new CurrencyChoice(MarketCurrency.Usd, "Доллар"));
        Currencies.Add(new CurrencyChoice(MarketCurrency.Eur, "Евро"));
        _selectedCurrency = Currencies[0];

        _library.Apps.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(RefreshAppChoices);
        _session.LoggedOut += () => Dispatcher.UIThread.Post(Reset);
        VisibleItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(IsEmpty));
        };

        RefreshAppChoices();
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
            return;

        _loaded = true;
        await LoadAsync();
    }

    [RelayCommand]
    private Task Refresh() => LoadAsync();

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var appId = SelectedApp?.AppId ?? 0;

        if (appId == 0)
            return;

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return;

        IsBusy = true;
        SellMessage = string.Empty;

        try
        {
            var contextId = InventoryService.ResolveContext(appId);
            var steamId = _session.OwnSteamId;
            var result = await _inventory.GetOwnAsync(appId, contextId, cancellationToken);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SelectedItem = null;
                HasDetail = false;
                HasPrices = false;
                Items.Clear();

                foreach (var snapshot in result.Items)
                {
                    var item = new InventoryItem(snapshot);
                    Items.Add(item);
                    _ = LoadIconAsync(item, snapshot.IconUrl);
                }

                ApplyFilter();
                StatusText = result.TotalCount > 0
                    ? $"Предметов: {result.Items.Count} из {result.TotalCount}"
                    : "Предметов нет";
            });
        }
        catch (HttpRequestException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Items.Clear();
                StatusText = "Не удалось загрузить инвентарь";
            });
        }
        catch (InvalidOperationException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Items.Clear();
                StatusText = "Сессия недоступна для загрузки инвентаря";
            });
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SellAsync(CancellationToken cancellationToken = default)
    {
        var item = SelectedItem;

        if (item == null || !item.CanSell || IsSelling)
            return;

        var cents = ParsePrice(SellPrice);

        if (cents <= 0)
        {
            SellMessage = "Введите цену, например 149,99";
            return;
        }

        IsSelling = true;
        SellMessage = string.Empty;

        try
        {
            var result = await _inventory.SellAsync(item.AppId, item.ContextId, item.AssetId, cents, _session.OwnSteamId, cancellationToken);

            if (result.Success)
            {
                SellMessage = result.RequiresConfirmation
                    ? "Предмет выставлен, подтвердите продажу в мобильном приложении Steam."
                    : "Предмет выставлен на торговую площадку.";
                await LoadAsync(cancellationToken);
            }
            else
            {
                SellMessage = result.Message.Length == 0 ? "Не удалось выставить предмет" : result.Message;
            }
        }
        finally
        {
            IsSelling = false;
        }
    }

    partial void OnSelectedItemChanged(InventoryItem? value)
    {
        HasDetail = value != null;
        HasPrices = false;
        DetailLowest = "—";
        DetailMedian = "—";
        SellPrice = string.Empty;
        SellMessage = string.Empty;

        if (value != null && value.CanSell)
            _ = LoadPricesAsync(value);
    }

    partial void OnOnlyMarketableChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        VisibleItems.Clear();

        foreach (var item in Items)
        {
            if (OnlyMarketable && !item.CanSell)
                continue;

            VisibleItems.Add(item);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private async Task LoadPricesAsync(InventoryItem item)
    {
        try
        {
            var overview = await _market.GetPriceOverviewAsync(item.AppId, item.HashName, SelectedCurrency?.Currency ?? MarketCurrency.Rub);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (SelectedItem != item || overview == null)
                    return;

                DetailLowest = overview.LowestPrice;
                DetailMedian = overview.MedianPrice;
                HasPrices = true;
            });
        }
        catch (HttpRequestException)
        {
        }
    }

    private async Task LoadIconAsync(InventoryItem item, string? iconUrl)
    {
        var bitmap = await _icons.LoadAsync(iconUrl);

        if (bitmap == null)
            return;

        await Dispatcher.UIThread.InvokeAsync(() => item.SetIcon(bitmap));
    }

    private void RefreshAppChoices()
    {
        var current = SelectedApp;
        var apps = new List<AppChoice>();

        foreach (var fallback in FallbackApps)
            apps.Add(new AppChoice(fallback.AppId, fallback.Name));

        foreach (var app in _library.Apps.Take(40))
        {
            if (!apps.Any(a => a.AppId == app.AppId))
                apps.Add(new AppChoice(app.AppId, app.Name));
        }

        Apps.Clear();

        foreach (var app in apps)
            Apps.Add(app);

        SelectedApp = current != null
            ? Apps.FirstOrDefault(a => a.AppId == current.AppId) ?? Apps.FirstOrDefault()
            : Apps.FirstOrDefault();
    }

    private static long ParsePrice(string text)
    {
        var normalized = text.Trim().Replace(',', '.');

        if (!double.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            return 0;

        return (long)Math.Round(value * 100);
    }

    private void Reset()
    {
        _loaded = false;
        Items.Clear();
        SelectedItem = null;
        HasDetail = false;
        StatusText = string.Empty;
    }

    partial void OnSelectedAppChanged(AppChoice? value)
    {
        if (_loaded && value != null)
            _ = LoadAsync();
    }
}

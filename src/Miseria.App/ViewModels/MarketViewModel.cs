using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miseria.App.Services;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class MarketViewModel : ObservableObject
{
    private const int PageSize = 25;

    private const long MinOrderPrice = 3;

    private readonly MarketService _market;
    private readonly ItemIconCache _icons;
    private bool _loaded;
    private int _busy;

    public ObservableCollection<MarketItem> Results { get; } = [];

    public ObservableCollection<MarketItem> Listings { get; } = [];

    public ObservableCollection<MarketItem> Orders { get; } = [];

    public ObservableCollection<AppChoice> Apps { get; } = [];

    public ObservableCollection<CurrencyChoice> Currencies { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private AppChoice? _selectedApp;

    [ObservableProperty]
    private CurrencyChoice? _selectedCurrency;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isMyListings;

    [ObservableProperty]
    private MarketItem? _selectedItem;

    [ObservableProperty]
    private bool _hasDetail;

    [ObservableProperty]
    private string _detailLowest = string.Empty;

    [ObservableProperty]
    private string _detailMedian = string.Empty;

    [ObservableProperty]
    private string _detailVolume = string.Empty;

    [ObservableProperty]
    private bool _hasListings;

    [ObservableProperty]
    private bool _hasOrders;

    [ObservableProperty]
    private bool _hasMyListingsError;

    [ObservableProperty]
    private string _listingsSummary = string.Empty;

    [ObservableProperty]
    private string _listingsEmptyText = string.Empty;

    [ObservableProperty]
    private bool _isOrderPanelOpen;

    [ObservableProperty]
    private string _orderPriceInput = string.Empty;

    [ObservableProperty]
    private string _orderQuantityInput = "1";

    [ObservableProperty]
    private string _orderTotalText = string.Empty;

    [ObservableProperty]
    private string _orderMessage = string.Empty;

    [ObservableProperty]
    private string _orderBookSummary = string.Empty;

    [ObservableProperty]
    private bool _isSubmittingOrder;

    private MarketOrderBook? _orderBook;

    public bool HasResults => Results.Count > 0;

    public bool CanSubmitOrder => !IsSubmittingOrder
        && TryParsePriceToCents(OrderPriceInput, out var price) && price >= MinOrderPrice
        && long.TryParse(OrderQuantityInput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) && quantity is >= 1 and <= 9999;

    public MarketViewModel(MarketService market, ItemIconCache icons)
    {
        _market = market;
        _icons = icons;

        Apps.Add(new AppChoice(0, "Все игры"));
        Apps.Add(new AppChoice(730, "Counter-Strike 2"));
        Apps.Add(new AppChoice(570, "Dota 2"));
        Apps.Add(new AppChoice(440, "Team Fortress 2"));
        Apps.Add(new AppChoice(252490, "Rust"));
        Apps.Add(new AppChoice(753, "Steam"));
        _selectedApp = Apps[0];

        Currencies.Add(new CurrencyChoice(MarketCurrency.Usd, "Доллар"));
        Currencies.Add(new CurrencyChoice(MarketCurrency.Eur, "Евро"));
        Currencies.Add(new CurrencyChoice(MarketCurrency.Rub, "Рубль"));
        _selectedCurrency = Currencies[0];

        Results.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasResults));

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IsSubmittingOrder) or nameof(OrderPriceInput) or nameof(OrderQuantityInput))
                OnPropertyChanged(nameof(CanSubmitOrder));
        };
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
            return;

        _loaded = true;
        await SearchAsync();
    }

    [RelayCommand]
    private Task Refresh() => IsMyListings ? RefreshListingsAsync() : SearchAsync();

    [RelayCommand]
    private async Task SearchAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return;

        IsBusy = true;
        SelectedItem = null;
        HasDetail = false;

        try
        {
            var query = SearchText.Trim();
            var appId = SelectedApp?.AppId ?? 0;
            var currency = SelectedCurrency?.Currency ?? MarketCurrency.Usd;

            var result = await _market.SearchAsync(appId, query, 0, PageSize, cancellationToken);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Results.Clear();

                foreach (var snapshot in result.Items)
                {
                    var item = new MarketItem(
                        snapshot.HashName,
                        snapshot.Name,
                        snapshot.PriceText ?? currency.Format(snapshot.SellPriceCents),
                        $"Продавцов: {snapshot.Listings}",
                        snapshot.AppName,
                        snapshot.AppId);
                    Results.Add(item);
                    _ = LoadIconAsync(item, snapshot.IconUrl);
                }

                StatusText = result.Items.Count == 0
                    ? "Ничего не найдено"
                    : $"Найдено предметов: {result.TotalCount}";
            });
        }
        catch (HttpRequestException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = "Не удалось загрузить торговую площадку");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshListingsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var listings = await _market.GetMyListingsAsync(cancellationToken);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                HasMyListingsError = false;
                Listings.Clear();

                foreach (var listing in listings.Listings)
                {
                    var item = new MarketItem(
                        string.Empty,
                        listing.Name,
                        (SelectedCurrency?.Currency ?? MarketCurrency.Usd).Format(listing.PriceCents),
                        "Объявление активно",
                        null,
                        0,
                        listing.ListingId);
                    Listings.Add(item);
                    _ = LoadIconAsync(item, listing.IconUrl);
                }

                Orders.Clear();

                foreach (var order in listings.BuyOrders)
                {
                    var item = new MarketItem(
                        order.HashName,
                        order.HashName,
                        (SelectedCurrency?.Currency ?? MarketCurrency.Usd).Format(order.PriceCents),
                        $"Количество: {order.Quantity}",
                        null,
                        0,
                        0,
                        order.OrderId);
                    Orders.Add(item);
                }

                HasListings = Listings.Count > 0;
                HasOrders = Orders.Count > 0;
                ListingsSummary = $"Объявлений: {listings.Listings.Count} · Ордеров: {listings.BuyOrders.Count}";
                ListingsEmptyText = HasListings || HasOrders
                    ? string.Empty
                    : "Объявлений пока нет. Выставите предмет на продажу на торговой площадке Steam.";
            });
        }
        catch (HttpRequestException)
        {
            ShowListingsError();
        }
        catch (InvalidOperationException)
        {
            ShowListingsError();
        }
    }

    partial void OnSelectedItemChanged(MarketItem? value)
    {
        HasDetail = false;
        ResetOrderPanel();

        if (value != null)
            _ = LoadDetailAsync(value);
    }

    partial void OnSelectedAppChanged(AppChoice? value)
    {
        if (_loaded && !IsMyListings)
            _ = SearchAsync();
    }

    partial void OnSelectedCurrencyChanged(CurrencyChoice? value)
    {
        if (_loaded)
            _ = IsMyListings ? RefreshListingsAsync() : SearchAsync();
    }

    partial void OnIsMyListingsChanged(bool value)
    {
        if (!_loaded)
            return;

        if (value)
            _ = RefreshListingsAsync();
        else
            _ = SearchAsync();
    }

    [RelayCommand]
    private async Task CancelListingAsync(MarketItem? item)
    {
        if (item == null || item.ListingId == 0 || item.IsBusy)
            return;

        item.IsBusy = true;

        try
        {
            await _market.CancelListingAsync(item.ListingId);
            ListingsSummary = $"Объявление {item.Name} снято";
            await RefreshListingsAsync();
        }
        catch (HttpRequestException)
        {
            ListingsSummary = "Не удалось снять объявление";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CancelOrderAsync(MarketItem? item)
    {
        if (item == null || item.OrderId == 0 || item.IsBusy)
            return;

        item.IsBusy = true;

        try
        {
            await _market.CancelBuyOrderAsync(item.OrderId);
            ListingsSummary = $"Ордер на {item.Name} отменён";
            await RefreshListingsAsync();
        }
        catch (HttpRequestException)
        {
            ListingsSummary = "Не удалось отменить ордер";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    [RelayCommand]
    private void StartBuyOrder()
    {
        var item = SelectedItem;
        var appId = item != null && item.AppId != 0 ? item.AppId : SelectedApp?.AppId ?? 0;

        if (item == null || appId == 0 || item.HashName.Length == 0)
        {
            OrderMessage = "Выберите предмет из поиска, чтобы создать ордер";
            return;
        }

        _orderBook = null;
        OrderMessage = string.Empty;
        OrderBookSummary = "Загрузка книги ордеров…";
        OrderQuantityInput = "1";
        OrderPriceInput = string.Empty;
        IsOrderPanelOpen = true;
        _ = LoadOrderBookAsync(appId, item.HashName);
    }

    [RelayCommand]
    private void CancelOrderPanel() => ResetOrderPanel();

    [RelayCommand]
    private async Task SubmitBuyOrderAsync(CancellationToken cancellationToken = default)
    {
        var item = SelectedItem;
        var appId = item != null && item.AppId != 0 ? item.AppId : SelectedApp?.AppId ?? 0;

        if (item == null || appId == 0 || item.HashName.Length == 0)
            return;

        if (!TryParsePriceToCents(OrderPriceInput, out var price) || price < MinOrderPrice)
        {
            OrderMessage = "Введите цену не меньше 0,03";
            return;
        }

        if (!long.TryParse(OrderQuantityInput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity) || quantity is < 1 or > 9999)
        {
            OrderMessage = "Количество должно быть от 1 до 9999";
            return;
        }

        IsSubmittingOrder = true;
        OrderMessage = "Отправка ордера…";

        try
        {
            var result = await _market.CreateBuyOrderAsync(appId, item.HashName, price * quantity, (int)quantity, SelectedCurrency?.Currency ?? MarketCurrency.Usd, cancellationToken);

            if (result.Success)
            {
                OrderMessage = string.Empty;
                StatusText = result.OrderId == 0
                    ? "Ордер на покупку создан"
                    : $"Ордер на покупку {result.OrderId} создан";
                ListingsSummary = $"Ордер на {item.Name} создан. Он появится в разделе «Мои объявления»";
                ResetOrderPanel();
                await RefreshListingsAsync(cancellationToken);
            }
            else
            {
                OrderMessage = result.Message;
            }
        }
        finally
        {
            IsSubmittingOrder = false;
        }
    }

    partial void OnOrderPriceInputChanged(string value) => RecomputeOrderTotal();

    partial void OnOrderQuantityInputChanged(string value) => RecomputeOrderTotal();

    private async Task LoadOrderBookAsync(uint appId, string hashName)
    {
        var currency = SelectedCurrency?.Currency ?? MarketCurrency.Usd;
        var book = await _market.GetOrderBookAsync(appId, hashName, currency);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!IsOrderPanelOpen)
                return;

            _orderBook = book;

            if (book == null)
            {
                OrderBookSummary = "Книга ордеров недоступна для этого предмета, введите цену вручную";
                return;
            }

            var buy = book.HighestBuyOrderCents == null ? "—" : currency.Format(book.HighestBuyOrderCents.Value);
            var sell = book.LowestSellOrderCents == null ? "—" : currency.Format(book.LowestSellOrderCents.Value);
            OrderBookSummary = $"Лучшая заявка: {buy} · Продажа от: {sell}";

            var prefill = book.LowestSellOrderCents ?? book.HighestBuyOrderCents;

            if (prefill != null && OrderPriceInput.Length == 0)
                OrderPriceInput = (prefill.Value / 100d).ToString("0.00", CultureInfo.InvariantCulture);
        });
    }

    private void RecomputeOrderTotal()
    {
        if (!TryParsePriceToCents(OrderPriceInput, out var price))
        {
            OrderTotalText = string.Empty;
            return;
        }

        if (!long.TryParse(OrderQuantityInput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity))
        {
            OrderTotalText = string.Empty;
            return;
        }

        var currency = SelectedCurrency?.Currency ?? MarketCurrency.Usd;
        OrderTotalText = $"Итого: {currency.Format(price * quantity)}";
    }

    private void ResetOrderPanel()
    {
        IsOrderPanelOpen = false;
        _orderBook = null;
        OrderMessage = string.Empty;
        OrderBookSummary = string.Empty;
        OrderTotalText = string.Empty;
        OrderPriceInput = string.Empty;
        OrderQuantityInput = "1";
    }

    private static bool TryParsePriceToCents(string text, out long cents)
    {
        cents = 0;

        var normalized = text.Trim().Replace(" ", string.Empty).Replace(',', '.');
        var trimmed = normalized.TrimEnd('.', '0');

        if (trimmed.Length == 0 || !double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return false;

        var rounded = (long)Math.Round(value * 100d);

        if (rounded <= 0)
            return false;

        cents = rounded;
        return true;
    }

    private void ShowListingsError()
    {
        Dispatcher.UIThread.Post(() =>
        {
            HasMyListingsError = true;
            HasListings = false;
            HasOrders = false;
            ListingsSummary = string.Empty;
            ListingsEmptyText = string.Empty;
        });
    }

    private async Task LoadIconAsync(MarketItem item, string? iconUrl)
    {
        var bitmap = await _icons.LoadAsync(iconUrl);

        if (bitmap == null)
            return;

        await Dispatcher.UIThread.InvokeAsync(() => item.SetIcon(bitmap));
    }

    private async Task LoadDetailAsync(MarketItem item)
    {
        var appId = item.AppId != 0 ? item.AppId : SelectedApp?.AppId ?? 0;

        if (appId == 0 || item.HashName.Length == 0)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                DetailLowest = "—";
                DetailMedian = "—";
                DetailVolume = "—";
                HasDetail = true;
            });
            return;
        }

        try
        {
            var overview = await _market.GetPriceOverviewAsync(appId, item.HashName, SelectedCurrency?.Currency ?? MarketCurrency.Usd);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (SelectedItem != item)
                    return;

                if (overview == null)
                {
                    HasDetail = false;
                    StatusText = "Цены недоступны";
                    return;
                }

                DetailLowest = overview.LowestPrice;
                DetailMedian = overview.MedianPrice;
                DetailVolume = overview.Volume;
                HasDetail = true;
            });
        }
        catch (HttpRequestException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (SelectedItem == item)
                    StatusText = "Не удалось получить цены";
            });
        }
    }
}

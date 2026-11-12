using System.Collections.ObjectModel;
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

    public bool HasResults => Results.Count > 0;

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

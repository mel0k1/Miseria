using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Miseria.App.Services;

namespace Miseria.App.ViewModels;

public sealed record AppChoice(uint AppId, string Name);

public sealed record CurrencyChoice(MarketCurrency Currency, string Name);

public partial class MarketItem : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _priceText;

    [ObservableProperty]
    private string _listingsText;

    [ObservableProperty]
    private Bitmap? _icon;

    [ObservableProperty]
    private bool _hasIcon;

    [ObservableProperty]
    private bool _isBusy;

    public string HashName { get; }

    public uint AppId { get; }

    public string? AppName { get; }

    public bool HasAppName => !string.IsNullOrWhiteSpace(AppName);

    public ulong ListingId { get; }

    public ulong OrderId { get; }

    public string MarketUrl => HashName.Length == 0
        ? "https://steamcommunity.com/market/"
        : $"https://steamcommunity.com/market/listings/{AppId}/{Uri.EscapeDataString(HashName)}";

    public MarketItem(string hashName, string name, string priceText, string listingsText, string? appName, uint appId = 0, ulong listingId = 0, ulong orderId = 0)
    {
        HashName = hashName;
        Name = name;
        PriceText = priceText;
        ListingsText = listingsText;
        AppName = appName;
        AppId = appId;
        ListingId = listingId;
        OrderId = orderId;
    }

    public void SetIcon(Bitmap? bitmap)
    {
        if (bitmap == null)
            return;

        Icon = bitmap;
        HasIcon = true;
    }
}

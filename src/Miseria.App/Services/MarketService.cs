using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;
using Miseria.Core;

namespace Miseria.App.Services;

public enum MarketCurrency
{
    Usd = 1,
    Eur = 3,
    Rub = 5
}

public static class MarketCurrencyExtensions
{
    public static string Symbol(this MarketCurrency currency) => currency switch
    {
        MarketCurrency.Usd => "$",
        MarketCurrency.Eur => "€",
        MarketCurrency.Rub => "₽",
        _ => string.Empty
    };

    public static bool SymbolBefore(this MarketCurrency currency) => currency is MarketCurrency.Usd or MarketCurrency.Eur;

    public static string Format(this MarketCurrency currency, long cents)
    {
        var amount = (cents / 100d).ToString("0.00", CultureInfo.InvariantCulture);

        return currency.SymbolBefore()
            ? $"{currency.Symbol()}{amount}"
            : $"{amount} {currency.Symbol()}";
    }
}

public sealed record MarketSearchResult(IReadOnlyList<MarketItemSnapshot> Items, int TotalCount);

public sealed record MarketItemSnapshot(uint AppId, string HashName, string Name, long SellPriceCents, string? PriceText, long Listings, string? IconUrl, string? AppName);

public sealed record MarketPriceOverview(string LowestPrice, string MedianPrice, string Volume);

public sealed record MyListingSnapshot(long PriceCents, string Name, string? IconUrl, ulong ListingId);

public sealed record MyBuyOrderSnapshot(long PriceCents, long Quantity, string HashName, ulong OrderId);

public sealed record MyListings(IReadOnlyList<MyListingSnapshot> Listings, IReadOnlyList<MyBuyOrderSnapshot> BuyOrders);

public sealed class MarketService
{
    private readonly SteamWebApi _web;

    public MarketService(SteamWebApi web) => _web = web;

    public async Task<MarketSearchResult> SearchAsync(uint appId, string query, int start, int count, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, string?>
        {
            ["norender"] = "1",
            ["query"] = query,
            ["start"] = start.ToString(CultureInfo.InvariantCulture),
            ["count"] = count.ToString(CultureInfo.InvariantCulture),
            ["search_descriptions"] = "0"
        };

        if (appId != 0)
            args["appid"] = appId.ToString(CultureInfo.InvariantCulture);

        var json = await _web.GetPublicCommunityJsonAsync("market/search/render/", args, cancellationToken);
        var items = new List<MarketItemSnapshot>();
        var total = json?["total_count"]?.GetValue<int>() ?? 0;

        if (json?["results"] is JsonArray results)
        {
            foreach (var node in results.OfType<JsonObject>())
            {
                var hash = node["hash_name"]?.GetValue<string>() ?? string.Empty;
                var asset = node["asset_description"] as JsonObject;
                var icon = asset?["icon_url"]?.GetValue<string>();
                var appName = node["app_name"]?.GetValue<string>();

                items.Add(new MarketItemSnapshot(
                    (uint)(asset?["appid"]?.GetValue<long>() ?? 0),
                    hash,
                    node["name"]?.GetValue<string>() ?? hash,
                    node["sell_price"]?.GetValue<long>() ?? 0,
                    node["sell_price_text"]?.GetValue<string>(),
                    node["sell_listings"]?.GetValue<long>() ?? 0,
                    icon,
                    appName));
            }
        }

        return new MarketSearchResult(items, total);
    }

    public async Task<MarketPriceOverview?> GetPriceOverviewAsync(uint appId, string hashName, MarketCurrency currency, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, string?>
        {
            ["appid"] = appId.ToString(CultureInfo.InvariantCulture),
            ["market_hash_name"] = hashName,
            ["currency"] = ((int)currency).ToString(CultureInfo.InvariantCulture)
        };

        var json = await _web.GetPublicCommunityJsonAsync("market/priceoverview/", args, cancellationToken);

        if (json?["success"]?.GetValue<bool>() != true)
            return null;

        return new MarketPriceOverview(
            json["lowest_price"]?.GetValue<string>() ?? "—",
            json["median_price"]?.GetValue<string>() ?? "—",
            json["volume"]?.GetValue<string>() ?? "0");
    }

    public async Task<MyListings> GetMyListingsAsync(CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, string?>
        {
            ["norender"] = "1",
            ["count"] = "100"
        };

        var json = await _web.GetCommunityJsonAsync("market/mylistings/", args, cancellationToken);

        var descriptions = new Dictionary<(long ClassId, long InstanceId), (string Name, string? Icon)>();

        if (json?["asset_descriptions"] is JsonArray descriptionsArray)
        {
            foreach (var node in descriptionsArray.OfType<JsonObject>())
            {
                var classId = ToLong(node["classid"]);
                var instanceId = ToLong(node["instanceid"]);

                if (classId == 0)
                    continue;

                descriptions[(classId, instanceId)] = (
                    node["market_name"]?.GetValue<string>() ?? node["name"]?.GetValue<string>() ?? "Предмет",
                    node["icon_url"]?.GetValue<string>());
            }
        }

        var listings = new List<MyListingSnapshot>();

        if (json?["listings"] is JsonArray listingsArray)
        {
            foreach (var node in listingsArray.OfType<JsonObject>())
            {
                var asset = node["asset"] as JsonObject;
                var classId = asset == null ? 0 : ToLong(asset["classid"]);
                var instanceId = asset == null ? 0 : ToLong(asset["instanceid"]);
                var price = (ToLong(node["price"]) + ToLong(node["fee"])) / 100;
                var known = descriptions.TryGetValue((classId, instanceId), out var description);

                listings.Add(new MyListingSnapshot(
                    price,
                    known ? description.Name : "Предмет",
                    known ? description.Icon : null,
                    (ulong)ToLong(node["listingid"])));
            }
        }

        var buyOrders = new List<MyBuyOrderSnapshot>();

        if (json?["buy_orders"] is JsonArray ordersArray)
        {
            foreach (var node in ordersArray.OfType<JsonObject>())
            {
                buyOrders.Add(new MyBuyOrderSnapshot(
                    ToLong(node["price"]),
                    ToLong(node["quantity"]),
                    node["hash_name"]?.GetValue<string>() ?? "—",
                    (ulong)ToLong(node["buy_orderid"])));
            }
        }

        return new MyListings(listings, buyOrders);
    }

    public async Task<bool> CancelListingAsync(ulong listingId, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string?>();

        await _web.PostFormAsync(
            $"https://steamcommunity.com/market/removelisting/{listingId.ToString(CultureInfo.InvariantCulture)}/",
            "https://steamcommunity.com/market/",
            form,
            true,
            cancellationToken);

        return true;
    }

    public async Task<bool> CancelBuyOrderAsync(ulong orderId, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string?>
        {
            ["buy_orderid"] = orderId.ToString(CultureInfo.InvariantCulture)
        };

        await _web.PostFormAsync(
            "https://steamcommunity.com/market/cancelbuyorder/",
            "https://steamcommunity.com/market/",
            form,
            true,
            cancellationToken);

        return true;
    }

    private static long ToLong(JsonNode? node) =>
        node == null ? 0 : long.TryParse(node.GetValue<string>(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0;
}

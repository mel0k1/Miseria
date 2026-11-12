using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
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

public sealed record MarketOrderRow(long PriceCents, long Quantity);

public sealed record MarketOrderBook(long? HighestBuyOrderCents, long? LowestSellOrderCents, IReadOnlyList<MarketOrderRow> BuyRows, IReadOnlyList<MarketOrderRow> SellRows);

public sealed record BuyOrderResult(bool Success, string Message, ulong OrderId);

public sealed class MarketService
{
    private static readonly Regex NameIdClassic = new(@"Market_LoadOrderSpread\(\s*(\d+)\s*\)", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex NameIdJson = new(@"item_nameid[""']?\s*[:=]\s*[""']?(\d+)", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    private readonly SteamWebApi _web;
    private readonly ConcurrentDictionary<string, long?> _nameIds = new();

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

    public async Task<MarketOrderBook?> GetOrderBookAsync(uint appId, string hashName, MarketCurrency currency, CancellationToken cancellationToken = default)
    {
        var nameId = await GetItemNameIdAsync(appId, hashName, cancellationToken);

        if (nameId == null)
            return null;

        var args = new Dictionary<string, string?>
        {
            ["country"] = "RU",
            ["language"] = "russian",
            ["currency"] = ((int)currency).ToString(CultureInfo.InvariantCulture),
            ["item_nameid"] = nameId.Value.ToString(CultureInfo.InvariantCulture)
        };

        try
        {
            var json = await _web.GetCommunityJsonAsync("market/itemordershistogram", args, ListingUrl(appId, hashName), cancellationToken);

            if (json?["success"]?.GetValue<long>() != 1)
                return null;

            return new MarketOrderBook(
                ParseCents(json["highest_buy_order"]),
                ParseCents(json["lowest_sell_order"]),
                ParseGraph(json["buy_order_graph"]),
                ParseGraph(json["sell_order_graph"]));
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<BuyOrderResult> CreateBuyOrderAsync(uint appId, string hashName, long priceTotalCents, int quantity, MarketCurrency currency, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string?>
        {
            ["currency"] = ((int)currency).ToString(CultureInfo.InvariantCulture),
            ["appid"] = appId.ToString(CultureInfo.InvariantCulture),
            ["market_hash_name"] = hashName,
            ["price_total"] = priceTotalCents.ToString(CultureInfo.InvariantCulture),
            ["tradefee_tax"] = "0",
            ["quantity"] = quantity.ToString(CultureInfo.InvariantCulture),
            ["first_name"] = string.Empty,
            ["last_name"] = string.Empty,
            ["billing_address"] = string.Empty,
            ["billing_address_two"] = string.Empty,
            ["billing_country"] = string.Empty,
            ["billing_city"] = string.Empty,
            ["billing_state"] = string.Empty,
            ["billing_postal_code"] = string.Empty,
            ["save_my_address"] = "0",
            ["confirmation"] = "0"
        };

        try
        {
            var json = await _web.PostFormAsync(ListingUrl(appId, hashName), ListingUrl(appId, hashName), form, true, cancellationToken);

            if (json?["success"]?.GetValue<long>() == 1)
            {
                return new BuyOrderResult(
                    true,
                    string.Empty,
                    (ulong)ToLong(json["buy_orderid"]));
            }

            var message = json?["message"]?.GetValue<string>();

            return new BuyOrderResult(
                false,
                string.IsNullOrWhiteSpace(message) ? "Steam не принял ордер. Проверьте цену и попробуйте позже." : message,
                0);
        }
        catch (HttpRequestException)
        {
            return new BuyOrderResult(false, "Steam отклонил запрос. Проверьте соединение и попробуйте снова.", 0);
        }
        catch (InvalidOperationException)
        {
            return new BuyOrderResult(false, "Сессия недоступна для создания ордера", 0);
        }
    }

    public async Task<long?> GetItemNameIdAsync(uint appId, string hashName, CancellationToken cancellationToken = default)
    {
        var key = $"{appId}/{hashName}";

        if (_nameIds.TryGetValue(key, out var cached))
            return cached;

        try
        {
            var html = await _web.GetSessionCommunityTextAsync(ListingUrl(appId, hashName), cancellationToken);
            var match = html == null ? null : NameIdClassic.Match(html);

            if (match == null || !match.Success)
                match = html == null ? null : NameIdJson.Match(html);

            var value = match is { Success: true } ? long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            var result = value > 0 ? value : (long?)null;

            _nameIds[key] = result;
            return result;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static string ListingUrl(uint appId, string hashName) =>
        $"https://steamcommunity.com/market/listings/{appId}/{Uri.EscapeDataString(hashName)}";

    private static long? ParseCents(JsonNode? node)
    {
        var text = node?.ToString();

        return long.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;
    }

    private static List<MarketOrderRow> ParseGraph(JsonNode? node)
    {
        var rows = new List<MarketOrderRow>();

        if (node is not JsonArray array)
            return rows;

        foreach (var row in array.OfType<JsonArray>())
        {
            var price = row.Count > 0 ? ToLong(row[0]) : 0;
            var quantity = row.Count > 1 ? ToLong(row[1]) : 0;

            if (price > 0 && quantity > 0)
                rows.Add(new MarketOrderRow(price, quantity));
        }

        return rows;
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

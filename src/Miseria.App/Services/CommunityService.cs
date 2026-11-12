using System.Globalization;
using System.Text.Json.Nodes;
using Miseria.Core;

namespace Miseria.App.Services;

public sealed record NewsSnapshot(string Title, string Url, string FeedLabel, DateTimeOffset Date, string Contents);

public sealed record StoreItemSnapshot(uint AppId, string Name, string Url, string? ImageUrl, int DiscountPercent, long FinalPriceCents, string Currency);

public sealed record CommunityFeed(IReadOnlyList<NewsSnapshot> News, IReadOnlyList<StoreItemSnapshot> Specials, IReadOnlyList<StoreItemSnapshot> TopSellers, IReadOnlyList<StoreItemSnapshot> NewReleases);

public sealed class CommunityService
{
    private readonly SteamWebApi _web;

    public CommunityService(SteamWebApi web) => _web = web;

    public async Task<IReadOnlyList<NewsSnapshot>> GetNewsAsync(uint appId, CancellationToken cancellationToken = default)
    {
        var url = $"https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid={appId}&count=8&maxlength=280";
        var json = await _web.GetPublicJsonAsync(url, cancellationToken);
        var news = new List<NewsSnapshot>();

        if (json?["appnews"]?["newsitems"] is JsonArray items)
        {
            foreach (var node in items.OfType<JsonObject>())
            {
                var title = node["title"]?.GetValue<string>() ?? string.Empty;

                if (title.Length == 0)
                    continue;

                news.Add(new NewsSnapshot(
                    title,
                    node["url"]?.GetValue<string>() ?? string.Empty,
                    node["feedlabel"]?.GetValue<string>() ?? string.Empty,
                    node["date"] == null ? DateTimeOffset.MinValue : DateTimeOffset.FromUnixTimeSeconds(node["date"]!.GetValue<long>()),
                    node["contents"]?.GetValue<string>() ?? string.Empty));
            }
        }

        return news;
    }

    public async Task<(IReadOnlyList<StoreItemSnapshot> Specials, IReadOnlyList<StoreItemSnapshot> TopSellers, IReadOnlyList<StoreItemSnapshot> NewReleases)> GetFeaturedAsync(CancellationToken cancellationToken = default)
    {
        var json = await _web.GetPublicJsonAsync("https://store.steampowered.com/api/featuredcategories/?l=russian", cancellationToken);
        return (
            ParseItems(json?["specials"]?["items"] as JsonArray),
            ParseItems(json?["top_sellers"]?["items"] as JsonArray),
            ParseItems(json?["new_releases"]?["items"] as JsonArray));
    }

    private static List<StoreItemSnapshot> ParseItems(JsonArray? array)
    {
        var items = new List<StoreItemSnapshot>();

        if (array == null)
            return items;

        foreach (var node in array.OfType<JsonObject>())
        {
            var name = node["name"]?.GetValue<string>() ?? string.Empty;
            var rawUrl = node["url"]?.GetValue<string>() ?? string.Empty;
            var url = rawUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? rawUrl
                : $"https://store.steampowered.com/{rawUrl.TrimStart('/')}";

            var appId = node["id"]?.GetValue<long>() ?? 0;
            var type = node["type"]?.GetValue<string>() ?? string.Empty;

            items.Add(new StoreItemSnapshot(
                type.Length == 0 || type == "app" ? (uint)appId : 0,
                name,
                url,
                node["header_image"]?.GetValue<string>() ?? node["large_capsule_image"]?.GetValue<string>() ?? node["small_capsule_image"]?.GetValue<string>(),
                node["discount_percent"]?.GetValue<int>() ?? 0,
                node["final_price"]?.GetValue<long>() ?? 0,
                node["currency"]?.GetValue<string>() ?? string.Empty));
        }

        return items;
    }
}

public static class StorePriceFormat
{
    public static string Format(long cents, string currency)
    {
        var amount = (cents / 100d).ToString("0.00", CultureInfo.InvariantCulture);

        return currency switch
        {
            "USD" => $"${amount}",
            "EUR" => $"€{amount}",
            "RUB" => $"{amount} ₽",
            "GBP" => $"£{amount}",
            "BRL" => $"R$ {amount}",
            _ => $"{amount} {currency}"
        };
    }
}

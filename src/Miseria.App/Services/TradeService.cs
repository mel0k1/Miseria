using System.Globalization;
using System.Text.Json.Nodes;
using Miseria.Core;

namespace Miseria.App.Services;

public enum TradeDirection
{
    Incoming,
    Sent
}

public sealed record TradeAssetSnapshot(uint AppId, long ClassId, long InstanceId, long AssetId);

public sealed record TradeDescriptionSnapshot(uint AppId, long ClassId, long InstanceId, string Name, string Type, string? IconUrl, string? NameColor);

public sealed record TradeOfferSnapshot(
    ulong OfferId,
    ulong PartnerId,
    bool IsOurs,
    TradeDirection Direction,
    string? Message,
    DateTimeOffset CreatedAt,
    bool NeedsMobileConfirmation,
    IReadOnlyList<TradeAssetSnapshot> ToGive,
    IReadOnlyList<TradeAssetSnapshot> ToReceive);

public sealed record TradeOffersResult(IReadOnlyList<TradeOfferSnapshot> Offers, IReadOnlyDictionary<(uint AppId, long ClassId, long InstanceId), TradeDescriptionSnapshot> Descriptions);

public sealed record TradeAcceptResult(bool Accepted, bool NeedsMobileConfirmation);

public sealed class TradeService
{
    private readonly SteamWebApi _web;

    public TradeService(SteamWebApi web) => _web = web;

    public async Task<TradeOffersResult> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, string?>
        {
            ["get_sent_offers"] = "1",
            ["get_received_offers"] = "1",
            ["get_descriptions"] = "1",
            ["language"] = "russian",
            ["active_only"] = "1",
            ["historical_only"] = "0"
        };

        var json = await _web.GetApiJsonAsync("IEconService/GetTradeOffers/v1/", args, cancellationToken);
        var response = json?["response"] as JsonObject ?? [];
        var descriptions = ParseDescriptions(response["descriptions"] as JsonArray);
        var offers = new List<TradeOfferSnapshot>();

        ParseOffers(response["trade_offers_received"] as JsonArray, TradeDirection.Incoming, offers);
        ParseOffers(response["trade_offers_sent"] as JsonArray, TradeDirection.Sent, offers);

        return new TradeOffersResult(offers, descriptions);
    }

    public async Task<TradeAcceptResult> AcceptAsync(ulong offerId, ulong partnerId, CancellationToken cancellationToken = default)
    {
        var json = await _web.PostCommunityFormAsync(
            $"tradeoffer/{offerId}/accept/",
            new Dictionary<string, string?>
            {
                ["serverid"] = "1",
                ["tradeofferid"] = offerId.ToString(CultureInfo.InvariantCulture),
                ["partner"] = partnerId.ToString(CultureInfo.InvariantCulture)
            },
            cancellationToken);

        return new TradeAcceptResult(
            json != null,
            json?["needs_mobile_confirmation"]?.GetValue<bool>() == true);
    }

    public async Task<bool> DeclineAsync(ulong offerId, CancellationToken cancellationToken = default)
    {
        var json = await _web.PostCommunityFormAsync(
            $"tradeoffer/{offerId}/decline/",
            new Dictionary<string, string?>(),
            cancellationToken);

        return json != null;
    }

    public async Task<bool> CancelAsync(ulong offerId, CancellationToken cancellationToken = default)
    {
        var json = await _web.PostCommunityFormAsync(
            $"tradeoffer/{offerId}/cancel/",
            new Dictionary<string, string?>(),
            cancellationToken);

        return json != null;
    }

    private static Dictionary<(uint AppId, long ClassId, long InstanceId), TradeDescriptionSnapshot> ParseDescriptions(JsonArray? array)
    {
        var map = new Dictionary<(uint, long, long), TradeDescriptionSnapshot>();

        if (array == null)
            return map;

        foreach (var node in array.OfType<JsonObject>())
        {
            var appId = (uint)(node["appid"]?.GetValue<long>() ?? 0);
            var classId = ToLong(node["classid"]);
            var instanceId = ToLong(node["instanceid"]);

            map[(appId, classId, instanceId)] = new TradeDescriptionSnapshot(
                appId,
                classId,
                instanceId,
                node["market_name"]?.GetValue<string>() ?? node["name"]?.GetValue<string>() ?? "Предмет",
                node["type"]?.GetValue<string>() ?? string.Empty,
                node["icon_url"]?.GetValue<string>(),
                node["name_color"]?.GetValue<string>());
        }

        return map;
    }

    private static void ParseOffers(JsonArray? array, TradeDirection direction, List<TradeOfferSnapshot> offers)
    {
        if (array == null)
            return;

        foreach (var node in array.OfType<JsonObject>())
        {
            var offerId = ToLong(node["tradeofferid"]);

            if (offerId == 0)
                continue;

            offers.Add(new TradeOfferSnapshot(
                (ulong)offerId,
                (ulong)ToLong(node["steamid_other"]),
                node["is_our_offer"]?.GetValue<bool>() ?? direction == TradeDirection.Sent,
                direction,
                node["message"]?.GetValue<string>(),
                ToDate(node["time_created"]),
                node["confirmation_method"]?.GetValue<long>() == 2,
                ParseAssets(node["items_to_give"] as JsonArray),
                ParseAssets(node["items_to_receive"] as JsonArray)));
        }
    }

    private static List<TradeAssetSnapshot> ParseAssets(JsonArray? array)
    {
        var assets = new List<TradeAssetSnapshot>();

        if (array == null)
            return assets;

        foreach (var node in array.OfType<JsonObject>())
        {
            assets.Add(new TradeAssetSnapshot(
                (uint)(node["appid"]?.GetValue<long>() ?? 0),
                ToLong(node["classid"]),
                ToLong(node["instanceid"]),
                ToLong(node["assetid"])));
        }

        return assets;
    }

    private static long ToLong(JsonNode? node) =>
        node == null ? 0 : long.TryParse(node.GetValue<string>(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static DateTimeOffset ToDate(JsonNode? node) =>
        node == null ? DateTimeOffset.MinValue : DateTimeOffset.FromUnixTimeSeconds(node.GetValue<long>());
}

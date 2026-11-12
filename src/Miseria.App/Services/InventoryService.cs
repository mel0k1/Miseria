using System.Globalization;
using System.Text.Json.Nodes;
using Miseria.Core;

namespace Miseria.App.Services;

public sealed record InventoryItemSnapshot(
    uint AppId,
    uint ContextId,
    string AssetId,
    long ClassId,
    long InstanceId,
    string Name,
    string MarketHashName,
    string Type,
    string? IconUrl,
    string? NameColor,
    bool Tradable,
    bool Marketable,
    string Description);

public sealed record InventoryResult(IReadOnlyList<InventoryItemSnapshot> Items, int TotalCount);

public sealed record SellResult(bool Success, string Message, bool RequiresConfirmation);

public sealed class InventoryService
{
    private readonly SteamWebApi _web;
    private readonly Func<Task<ulong>> _steamId;

    public InventoryService(SteamWebApi web, Func<Task<ulong>> steamId)
    {
        _web = web;
        _steamId = steamId;
    }

    public static uint ResolveContext(uint appId) => appId == 753 ? 6u : 2u;

    public async Task<InventoryResult> GetOwnAsync(uint appId, uint contextId, CancellationToken cancellationToken = default)
        => await GetAsync(await _steamId(), appId, contextId, cancellationToken);

    public async Task<InventoryResult> GetAsync(ulong steamId, uint appId, uint contextId, CancellationToken cancellationToken = default)
    {
        var items = new List<InventoryItemSnapshot>();
        var total = 0;
        var start = 0;

        while (true)
        {
            var args = new Dictionary<string, string?>
            {
                ["l"] = "russian",
                ["count"] = "2000"
            };

            if (start > 0)
                args["start_assetid"] = start.ToString(CultureInfo.InvariantCulture);

            var json = await _web.GetCommunityJsonAsync(
                $"inventory/{steamId}/{appId}/{contextId}",
                args,
                $"https://steamcommunity.com/inventory/{steamId}/{appId}/{contextId}",
                cancellationToken);

            if (json == null || json["success"]?.GetValue<long>() != 1)
                break;

            total = json["total_inventory_count"]?.GetValue<int>() ?? total;
            ParseAssets(json["assets"] as JsonArray, json["descriptions"] as JsonArray, appId, contextId, items);

            var more = json["more_start"]?.GetValue<string>();

            if (string.IsNullOrEmpty(more) || !long.TryParse(more, NumberStyles.Any, CultureInfo.InvariantCulture, out var next) || next == 0)
                break;

            start = (int)Math.Min(next, int.MaxValue);
        }

        return new InventoryResult(items, total);
    }

    public async Task<InventoryResult> GetPartnerAsync(ulong partnerSteamId, uint appId, uint contextId, CancellationToken cancellationToken = default)
    {
        var account = partnerSteamId & 0xFFFFFFFF;
        var items = new List<InventoryItemSnapshot>();
        var total = 0;
        var start = 0;

        while (true)
        {
            var args = new Dictionary<string, string?>
            {
                ["partner"] = partnerSteamId.ToString(CultureInfo.InvariantCulture),
                ["appid"] = appId.ToString(CultureInfo.InvariantCulture),
                ["contextid"] = contextId.ToString(CultureInfo.InvariantCulture)
            };

            if (start > 0)
                args["start"] = start.ToString(CultureInfo.InvariantCulture);

            var json = await _web.GetCommunityJsonAsync(
                "tradeoffer/new/partnerinventory/",
                args,
                $"https://steamcommunity.com/tradeoffer/new/?partner={account}",
                cancellationToken);

            if (json == null || json["success"]?.GetValue<bool>() != true)
                break;

            ParseRgInventory(json["rgInventory"] as JsonObject, json["rgDescriptions"] as JsonObject, appId, contextId, items, out var count);
            total += count;

            if (json["more"]?.GetValue<bool>() != true)
                break;

            start = (int)(json["more_start"]?.GetValue<long>() ?? 0);

            if (start == 0)
                break;
        }

        return new InventoryResult(items, total);
    }

    public async Task<SellResult> SellAsync(uint appId, uint contextId, string assetId, long buyerPriceCents, ulong steamId, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string?>
        {
            ["appid"] = appId.ToString(CultureInfo.InvariantCulture),
            ["contextid"] = contextId.ToString(CultureInfo.InvariantCulture),
            ["assetid"] = assetId,
            ["amount"] = "1",
            ["price"] = buyerPriceCents.ToString(CultureInfo.InvariantCulture)
        };

        var referer = $"https://steamcommunity.com/inventory/{steamId}/{appId}/{contextId}";

        try
        {
            var json = await _web.PostFormAsync("https://steamcommunity.com/market/sellitem/", referer, form, true, cancellationToken);

            if (json == null)
                return new SellResult(false, "Steam вернул пустой ответ", false);

            var success = json["success"]?.GetValue<bool>() == true;
            var message = json["message"]?.GetValue<string>() ?? string.Empty;

            return new SellResult(success, message, json["requires_confirmation"]?.GetValue<long>() == 1);
        }
        catch (HttpRequestException)
        {
            return new SellResult(false, "Steam отклонил запрос", false);
        }
    }

    private static void ParseAssets(JsonArray? assets, JsonArray? descriptions, uint appId, uint contextId, List<InventoryItemSnapshot> items)
    {
        if (assets == null || descriptions == null)
            return;

        var map = new Dictionary<(long ClassId, long InstanceId), InventoryItemSnapshot>();

        foreach (var node in descriptions.OfType<JsonObject>())
        {
            var classId = ToLong(node["classid"]);
            var instanceId = ToLong(node["instanceid"]);
            var description = ParseDescription(node, appId, contextId);
            map[(classId, instanceId)] = description;
        }

        foreach (var node in assets.OfType<JsonObject>())
        {
            var classId = ToLong(node["classid"]);
            var instanceId = ToLong(node["instanceid"]);
            var assetId = node["assetid"]?.GetValue<string>() ?? string.Empty;

            if (assetId.Length == 0 || !map.TryGetValue((classId, instanceId), out var description))
                continue;

            items.Add(description with { AssetId = assetId });
        }
    }

    private static void ParseRgInventory(JsonObject? inventory, JsonObject? descriptions, uint appId, uint contextId, List<InventoryItemSnapshot> items, out int count)
    {
        count = 0;

        if (inventory == null)
            return;

        foreach (var pair in inventory)
        {
            if (pair.Value is not JsonObject node)
                continue;

            var classId = ToLong(node["classid"]);
            var instanceId = ToLong(node["instanceid"]);
            var assetId = node["id"]?.GetValue<string>() ?? node["assetid"]?.GetValue<string>() ?? string.Empty;

            if (assetId.Length == 0)
                continue;

            var known = descriptions != null
                && descriptions[$"{classId}_{instanceId}"] is JsonObject descriptionNode
                    ? ParseDescription(descriptionNode, appId, contextId)
                    : null;

            items.Add(known ?? new InventoryItemSnapshot(appId, contextId, assetId, classId, instanceId, "Предмет", string.Empty, string.Empty, null, null, false, false, string.Empty));
            count++;
        }
    }

    private static InventoryItemSnapshot ParseDescription(JsonObject node, uint appId, uint contextId)
    {
        var assetId = string.Empty;
        var description = string.Empty;

        if (node["descriptions"] is JsonArray details)
        {
            var parts = details.OfType<JsonObject>()
                .Select(d => d["value"]?.GetValue<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Take(3)
                .ToList();

            description = string.Join(" · ", parts);
        }

        return new InventoryItemSnapshot(
            appId,
            contextId,
            assetId,
            ToLong(node["classid"]),
            ToLong(node["instanceid"]),
            node["market_name"]?.GetValue<string>() ?? node["name"]?.GetValue<string>() ?? "Предмет",
            node["market_hash_name"]?.GetValue<string>() ?? string.Empty,
            node["type"]?.GetValue<string>() ?? string.Empty,
            node["icon_url"]?.GetValue<string>(),
            node["name_color"]?.GetValue<string>(),
            node["tradable"]?.GetValue<long>() == 1,
            node["marketable"]?.GetValue<long>() == 1,
            description);
    }

    private static long ToLong(JsonNode? node) =>
        node == null ? 0 : long.TryParse(node.GetValue<string>(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0;
}

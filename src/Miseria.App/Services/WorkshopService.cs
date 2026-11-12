using System.Globalization;
using System.Text.Json.Nodes;
using Miseria.Core;

namespace Miseria.App.Services;

public sealed record WorkshopFileSnapshot(
    string FileId,
    uint AppId,
    string Title,
    string Description,
    string? PreviewUrl,
    long FileSize,
    string CreatorId,
    DateTimeOffset Created,
    DateTimeOffset Updated,
    long Subscriptions,
    long Favorited,
    long Views,
    string Tags,
    string CreatorName);

public sealed record WorkshopResult(IReadOnlyList<WorkshopFileSnapshot> Items, int TotalCount);

public sealed class WorkshopService
{
    private readonly SteamWebApi _web;

    public WorkshopService(SteamWebApi web) => _web = web;

    public async Task<WorkshopResult> QueryAsync(uint appId, string searchText, int page, int count, CancellationToken cancellationToken = default)
    {
        var args = new Dictionary<string, string?>
        {
            ["appid"] = appId.ToString(CultureInfo.InvariantCulture),
            ["query_type"] = searchText.Length == 0 ? "11" : "9",
            ["page"] = Math.Max(page, 1).ToString(CultureInfo.InvariantCulture),
            ["numperpage"] = count.ToString(CultureInfo.InvariantCulture),
            ["search_text"] = searchText,
            ["filetype"] = "0",
            ["return_details"] = "true",
            ["return_metadata"] = "true",
            ["return_short_description"] = "true",
            ["noredirect"] = "1"
        };

        var json = await _web.GetApiJsonAsync("IPublishedFileService/QueryFiles/v1/", args, cancellationToken);
        var response = json?["response"] as JsonObject ?? [];
        var total = response["totalcount"]?.GetValue<int>() ?? 0;
        var items = new List<WorkshopFileSnapshot>();

        if (response["publishedfiledetails"] is JsonArray files)
        {
            var creators = files.OfType<JsonObject>()
                .Select(f => f["creator"]?.GetValue<string>() ?? string.Empty)
                .Where(id => id.Length > 0)
                .Distinct()
                .ToList();

            var names = await ResolveNamesAsync(creators, cancellationToken);

            foreach (var node in files.OfType<JsonObject>())
            {
                var fileId = node["publishedfileid"]?.GetValue<string>() ?? string.Empty;

                if (fileId.Length == 0 || node["result"]?.GetValue<int>() is not (1 or 9))
                    continue;

                var title = node["title"]?.GetValue<string>() ?? "Без названия";
                var description = node["short_description"]?.GetValue<string>()
                    ?? node["description"]?.GetValue<string>()
                    ?? string.Empty;
                var creator = node["creator"]?.GetValue<string>() ?? string.Empty;
                var tags = node["tags"] is JsonArray tagsArray
                    ? string.Join(", ", tagsArray.OfType<JsonObject>()
                        .Select(t => t["tag"]?.GetValue<string>())
                        .Where(t => !string.IsNullOrWhiteSpace(t)))
                    : string.Empty;

                items.Add(new WorkshopFileSnapshot(
                    fileId,
                    (uint)(node["consumer_appid"]?.GetValue<long>() ?? appId),
                    title,
                    description,
                    node["preview_url"]?.GetValue<string>(),
                    node["file_size"]?.GetValue<long>() ?? 0,
                    creator,
                    ToDate(node["time_created"]),
                    ToDate(node["time_updated"]),
                    node["subscriptions"]?.GetValue<long>() ?? 0,
                    node["favorited"]?.GetValue<long>() ?? 0,
                    node["views"]?.GetValue<long>() ?? 0,
                    tags,
                    names.TryGetValue(creator, out var name) ? name : string.Empty));
            }
        }

        return new WorkshopResult(items, total);
    }

    public async Task<bool> SubscribeAsync(uint appId, string fileId, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string?>
        {
            ["id"] = fileId,
            ["appid"] = appId.ToString(CultureInfo.InvariantCulture)
        };

        await _web.PostFormAsync("https://steamcommunity.com/sharedfiles/subscribe", null, form, true, cancellationToken);
        return true;
    }

    public async Task<bool> UnsubscribeAsync(uint appId, string fileId, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string?>
        {
            ["id"] = fileId,
            ["appid"] = appId.ToString(CultureInfo.InvariantCulture)
        };

        await _web.PostFormAsync("https://steamcommunity.com/sharedfiles/unsubscribe", null, form, true, cancellationToken);
        return true;
    }

    private async Task<Dictionary<string, string>> ResolveNamesAsync(List<string> steamIds, CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string>();

        if (steamIds.Count == 0)
            return names;

        try
        {
            var args = new Dictionary<string, string?> { ["steamids"] = string.Join(",", steamIds) };
            var json = await _web.GetApiJsonAsync("ISteamUser/GetPlayerSummaries/v2/", args, cancellationToken);

            if (json?["response"]?["players"] is JsonArray players)
            {
                foreach (var player in players.OfType<JsonObject>())
                {
                    var id = player["steamid"]?.GetValue<string>();
                    var name = player["personaname"]?.GetValue<string>();

                    if (id != null && name != null)
                        names[id] = name;
                }
            }
        }
        catch (HttpRequestException)
        {
        }

        return names;
    }

    private static DateTimeOffset ToDate(JsonNode? node) =>
        node == null ? DateTimeOffset.MinValue : DateTimeOffset.FromUnixTimeSeconds(node.GetValue<long>());
}

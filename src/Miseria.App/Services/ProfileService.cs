using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Miseria.Core;

namespace Miseria.App.Services;

public sealed record FriendGameSnapshot(uint AppId, string Name, double Hours);

public sealed record ProfileScreenshotSnapshot(string FileId, string PreviewUrl, string DetailsUrl);

public sealed record FriendProfile(
    bool IsPrivate,
    string? RealName,
    string? Location,
    DateTimeOffset? MemberSince,
    int? Level,
    string? Summary,
    int? GamesCount,
    double TotalHours,
    IReadOnlyList<FriendGameSnapshot> Games,
    IReadOnlyList<ProfileScreenshotSnapshot> Screenshots);

public sealed class ProfileService
{
    private const int MaxGames = 10;
    private const int MaxScreenshots = 12;

    private static readonly Regex LevelRegex = new(@"friendPlayerLevelNum"">(\d+)", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex SummaryRegex = new(@"profile_summary"">\s*(.+?)\s*</div>", RegexOptions.Compiled | RegexOptions.Singleline, TimeSpan.FromSeconds(5));
    private static readonly Regex LocationRegex = new(@"header_location"">\s*<img[^>]*>\s*([^<]+)", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex RealNameRegex = new(@"header_real_name[^>]*>\s*<bdi>([^<]+)</bdi>", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex ShotIdRegex = new(@"data-publishedfileid=""(\d+)""", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex ShotImageRegex = new(@"background-image:\s*url\('([^']+)'\)", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex LineBreaksRegex = new(@"<br\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5));
    private static readonly Regex TagsRegex = new(@"<[^>]+>", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex SpacesRegex = new(@"\s+", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    private readonly SteamWebApi _web;

    public ProfileService(SteamWebApi web) => _web = web;

    public async Task<FriendProfile?> LoadAsync(ulong steamId, bool isOwn, CancellationToken cancellationToken = default)
    {
        var summaries = await LoadSummariesAsync(steamId, cancellationToken);

        if (!isOwn && summaries.Visibility != 3)
            return new FriendProfile(true, summaries.RealName, summaries.Country, summaries.MemberSince, null, null, null, 0, [], []);

        var page = await LoadPageAsync(steamId, cancellationToken);
        var games = await LoadGamesAsync(steamId, cancellationToken);
        var screenshots = await LoadScreenshotsAsync(steamId, cancellationToken);

        return new FriendProfile(
            false,
            summaries.RealName ?? page?.RealName,
            page?.Location ?? summaries.Country,
            summaries.MemberSince,
            page?.Level,
            page?.Summary,
            games?.Count,
            games?.TotalHours ?? 0,
            games?.Games ?? [],
            screenshots);
    }

    private async Task<(string? RealName, string? Country, DateTimeOffset? MemberSince, int Visibility)> LoadSummariesAsync(
        ulong steamId, CancellationToken cancellationToken)
    {
        try
        {
            var json = await _web.GetApiJsonAsync(
                "ISteamUser/GetPlayerSummaries/v2/",
                new Dictionary<string, string?> { ["steamids"] = steamId.ToString(CultureInfo.InvariantCulture) },
                cancellationToken);

            if (json?["response"]?["players"] is not JsonArray players || players.Count == 0)
                return (null, null, null, 0);

            var player = players[0] as JsonObject;
            var visibility = player?["communityvisibilitystate"]?.GetValue<int>() ?? 0;
            var created = player?["timecreated"] == null
                ? null
                : (DateTimeOffset?)DateTimeOffset.FromUnixTimeSeconds(player!["timecreated"]!.GetValue<long>());

            return (
                NonEmpty(player?["realname"]?.GetValue<string>()),
                NonEmpty(player?["loccountrycode"]?.GetValue<string>()),
                created,
                visibility);
        }
        catch (HttpRequestException)
        {
            return (null, null, null, 0);
        }
    }

    private async Task<(int? Level, string? Summary, string? Location, string? RealName)?> LoadPageAsync(
        ulong steamId, CancellationToken cancellationToken)
    {
        try
        {
            var html = await _web.GetSessionCommunityTextAsync(
                $"https://steamcommunity.com/profiles/{steamId}",
                cancellationToken) ?? string.Empty;

            var level = LevelRegex.Match(html);
            var summary = SummaryRegex.Match(html);
            var location = LocationRegex.Match(html);
            var realName = RealNameRegex.Match(html);

            return (
                level.Success ? int.Parse(level.Groups[1].Value, CultureInfo.InvariantCulture) : null,
                summary.Success ? Clean(summary.Groups[1].Value) : null,
                location.Success ? Clean(location.Groups[1].Value) : null,
                realName.Success ? Clean(realName.Groups[1].Value) : null);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task<(int? Count, double TotalHours, IReadOnlyList<FriendGameSnapshot> Games)?> LoadGamesAsync(
        ulong steamId, CancellationToken cancellationToken)
    {
        try
        {
            var json = await _web.GetApiJsonAsync(
                "IPlayerService/GetOwnedGames/v1/",
                new Dictionary<string, string?>
                {
                    ["steamid"] = steamId.ToString(CultureInfo.InvariantCulture),
                    ["include_appinfo"] = "true",
                    ["include_played_free_games"] = "true"
                },
                cancellationToken);

            if (json?["response"] is not JsonObject response)
                return null;

            var count = response["game_count"]?.GetValue<int>();
            var games = new List<FriendGameSnapshot>();

            if (response["games"] is JsonArray array)
            {
                foreach (var node in array.OfType<JsonObject>())
                {
                    var name = node["name"]?.GetValue<string>() ?? string.Empty;
                    var minutes = node["playtime_forever"]?.GetValue<long>() ?? 0;

                    if (name.Length > 0 && minutes > 0)
                        games.Add(new FriendGameSnapshot(
                            (uint)(node["appid"]?.GetValue<long>() ?? 0),
                            name,
                            minutes / 60d));
                }
            }

            games = games.OrderByDescending(g => g.Hours).Take(MaxGames).ToList();

            return (count ?? (games.Count > 0 ? games.Count : null), games.Sum(g => g.Hours), games);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<ProfileScreenshotSnapshot>> LoadScreenshotsAsync(
        ulong steamId, CancellationToken cancellationToken)
    {
        try
        {
            var html = await _web.GetSessionCommunityTextAsync(
                $"https://steamcommunity.com/profiles/{steamId}/screenshots?p=1&appid=0&browsefilter=newestfirst&view=grid",
                cancellationToken) ?? string.Empty;

            var shots = new List<ProfileScreenshotSnapshot>();

            foreach (Match id in ShotIdRegex.Matches(html))
            {
                var window = html.Substring(id.Index, Math.Min(600, html.Length - id.Index));
                var image = ShotImageRegex.Match(window);

                if (!image.Success)
                    continue;

                shots.Add(new ProfileScreenshotSnapshot(
                    id.Groups[1].Value,
                    image.Groups[1].Value.Replace("\\/", "/"),
                    $"https://steamcommunity.com/sharedfiles/filedetails/?id={id.Groups[1].Value}"));

                if (shots.Count >= MaxScreenshots)
                    break;
            }

            return shots;
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    private static string Clean(string source) =>
        SpacesRegex.Replace(WebUtility.HtmlDecode(LineBreaksRegex.Replace(source, " ")), " ").Trim();

    private static string? NonEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

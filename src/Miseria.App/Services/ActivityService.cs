using System.Net;
using System.Text.RegularExpressions;
using Miseria.Core;

namespace Miseria.App.Services;

public sealed record ActivitySnapshot(string ActorName, string ProfileUrl, string? AvatarUrl, string Text, string TimeText, DateTimeOffset? Time);

public sealed class ActivityService
{
    private static readonly Regex BlockSplit = new(@"class=""blotter_block", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex AuthorLink = new(@"<a\s+class=""blotter_authorlink""\s+href=""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.Compiled | RegexOptions.Singleline, TimeSpan.FromSeconds(5));
    private static readonly Regex GroupLink = new(@"<a\s+class=""blotter_group_name""\s+href=""([^""]+)""[^>]*>(.*?)</a>", RegexOptions.Compiled | RegexOptions.Singleline, TimeSpan.FromSeconds(5));
    private static readonly Regex AvatarImage = new(@"<img\s+src=""(https://[^""]*(?:avatars|community/public/images)[^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5));
    private static readonly Regex StatSpan = new(@"<span\s+class=""blotter_stat"">(.*?)</span>", RegexOptions.Compiled | RegexOptions.Singleline, TimeSpan.FromSeconds(5));
    private static readonly Regex AbsTime = new(@"data-abstime=""(\d+)""", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    private const int MaxEntries = 40;
    private const int MaxTextLength = 280;

    private readonly SteamWebApi _web;

    public ActivityService(SteamWebApi web) => _web = web;

    public async Task<IReadOnlyList<ActivitySnapshot>> GetFeedAsync(CancellationToken cancellationToken = default)
    {
        var html = await _web.GetSessionCommunityTextAsync("https://steamcommunity.com/my/home/", cancellationToken);

        if (string.IsNullOrWhiteSpace(html))
            return [];

        return Parse(html);
    }

    public static IReadOnlyList<ActivitySnapshot> Parse(string html)
    {
        var entries = new List<ActivitySnapshot>();

        foreach (var raw in BlockSplit.Split(html).Skip(1))
        {
            var block = raw;

            var end = block.IndexOf(@"class=""blotter_block", StringComparison.Ordinal);

            if (end >= 0)
                block = block[..end];

            var author = ExtractLink(block, AuthorLink) ?? ExtractLink(block, GroupLink);

            if (author == null)
                continue;

            var avatar = AvatarImage.Match(block);
            var stat = StatSpan.Match(block);
            var absTime = AbsTime.Match(block);
            var time = absTime.Success && long.TryParse(absTime.Groups[1].Value, out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime()
                : (DateTimeOffset?)null;
            var text = BuildText(block, author.Name);

            if (text.Length == 0)
                continue;

            entries.Add(new ActivitySnapshot(
                author.Name,
                author.Url,
                avatar.Success ? WebUtility.HtmlDecode(avatar.Groups[1].Value) : null,
                text,
                time == null ? WebUtility.HtmlDecode(stat.Success ? stat.Groups[1].Value.Trim() : string.Empty) : RuFormat.DayTime(time.Value),
                time));

            if (entries.Count >= MaxEntries)
                break;
        }

        return entries;
    }

    private static LinkSnapshot? ExtractLink(string block, Regex regex)
    {
        var match = regex.Match(block);

        if (!match.Success)
            return null;

        var name = CleanText(match.Groups[2].Value);

        return name.Length == 0 ? null : new LinkSnapshot(WebUtility.HtmlDecode(match.Groups[1].Value), name);
    }

    private static string BuildText(string block, string actorName)
    {
        var text = CleanText(Tags.Replace(block, " "));
        var marker = text.IndexOf(actorName, StringComparison.Ordinal);

        if (marker == 0)
            text = text[actorName.Length..].TrimStart(' ', ',', ':', '.', '·', '-');

        if (text.Length > MaxTextLength)
            text = text[..MaxTextLength].TrimEnd() + "…";

        return text;
    }

    private static string CleanText(string source) =>
        Spaces.Replace(WebUtility.HtmlDecode(source), " ").Trim();

    private sealed record LinkSnapshot(string Url, string Name);
}

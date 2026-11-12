using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Miseria.Core;

namespace Miseria.App.Services;

public sealed record GroupSnapshot(string GroupId, string ShortName, string Name, string Headline, string Summary, string? AvatarUrl, int MemberCount, int OnlineCount, int InGameCount);

public sealed record GroupAnnouncementSnapshot(string Title, string Url, DateTimeOffset Date, string Text);

public sealed record GroupDiscussionSnapshot(string Title, string Url, string Author, int Replies, DateTimeOffset? LastPost);

public sealed class GroupService
{
    private static readonly Regex TopicSplit = new(@"class=""forum_topic(?=[\s""])", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex TopicContainer = new(@"class=""forum_topic[\s""]", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex TopicUrl = new(@"class=""forum_topic_overlay""\s+href=""([^""]+)""", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex TopicName = new(@"class=""forum_topic_name[^""]*"">\s*(.*?)\s*</div>", RegexOptions.Compiled | RegexOptions.Singleline, TimeSpan.FromSeconds(5));
    private static readonly Regex TopicAuthor = new(@"class=""forum_topic_op[^""]*"">\s*(.*?)\s*</div>", RegexOptions.Compiled | RegexOptions.Singleline, TimeSpan.FromSeconds(5));
    private static readonly Regex TopicReplies = new(@"class=""forum_topic_reply_count"">\s*<img[^>]*>\s*(\d+)", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex TopicTimestamp = new(@"data-timestamp=""(\d+)""", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex Tags = new(@"<[^>]+>", RegexOptions.Compiled, TimeSpan.FromSeconds(5));
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    private const int MaxDiscussions = 30;
    private const int MaxTextLength = 300;

    private readonly SteamWebApi _web;

    public GroupService(SteamWebApi web) => _web = web;

    public async Task<IReadOnlyList<GroupSnapshot>> GetOwnAsync(CancellationToken cancellationToken = default)
    {
        var json = await _web.GetApiJsonAsync("ISteamUser/GetUserGroupList/v1/", new Dictionary<string, string?>(), cancellationToken);
        var ids = new List<string>();

        if (json?["response"]?["groups"] is JsonArray groups)
        {
            foreach (var node in groups.OfType<JsonObject>())
            {
                var gid = node["gid"]?.GetValue<string>() ?? string.Empty;

                if (gid.Length > 0)
                    ids.Add(gid);
            }
        }

        var result = new List<GroupSnapshot>();

        foreach (var gid in ids.Take(15))
        {
            var details = await TryGetByGidAsync(gid, cancellationToken);

            if (details != null)
                result.Add(details);
        }

        return result;
    }

    public async Task<GroupSnapshot?> GetByLinkAsync(string query, CancellationToken cancellationToken = default)
    {
        var shortName = ExtractShortName(query);

        if (shortName.Length == 0)
            return null;

        try
        {
            var xml = await _web.GetCommunityXmlAsync($"https://steamcommunity.com/groups/{Uri.EscapeDataString(shortName)}/memberslistxml/?xml=1", cancellationToken);
            return ParseMembersList(xml);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<GroupAnnouncementSnapshot>> GetAnnouncementsAsync(GroupSnapshot group, CancellationToken cancellationToken = default)
    {
        var url = GroupUrl(group, "rss/");

        try
        {
            var xml = await _web.GetCommunityXmlAsync(url, cancellationToken);
            var announcements = new List<GroupAnnouncementSnapshot>();

            if (string.IsNullOrWhiteSpace(xml))
                return announcements;

            var document = XDocument.Parse(xml);
            var channel = document.Root?.Element("channel");

            if (channel == null)
                return announcements;

            foreach (var item in channel.Elements("item"))
            {
                var title = CleanText(item.Element("title")?.Value ?? string.Empty);
                var link = TrimValue(item.Element("link")?.Value);
                var text = CleanText(Tags.Replace(WebUtility.HtmlDecode(item.Element("description")?.Value ?? string.Empty), " "));

                if (text.Length > MaxTextLength)
                    text = text[..MaxTextLength].TrimEnd() + "…";

                if (title.Length == 0 || link.Length == 0)
                    continue;

                var published = DateTimeOffset.TryParse(
                    item.Element("pubDate")?.Value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed)
                    ? parsed.ToLocalTime()
                    : DateTimeOffset.MinValue;

                announcements.Add(new GroupAnnouncementSnapshot(title, link, published, text));
            }

            return announcements;
        }
        catch (HttpRequestException)
        {
            return [];
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<GroupDiscussionSnapshot>> GetDiscussionsAsync(GroupSnapshot group, CancellationToken cancellationToken = default)
    {
        var url = GroupUrl(group, "discussions");

        try
        {
            var html = await _web.GetSessionCommunityTextAsync(url, cancellationToken);
            var topics = new List<GroupDiscussionSnapshot>();

            if (string.IsNullOrWhiteSpace(html))
                return topics;

            foreach (var raw in TopicSplit.Split(html).Skip(1))
            {
                var block = raw;

                var next = TopicContainer.Match(block);

                if (next.Success)
                    block = block[..next.Index];

                var link = TopicUrl.Match(block);
                var name = TopicName.Match(block);
                var author = TopicAuthor.Match(block);
                var replies = TopicReplies.Match(block);
                var stamp = TopicTimestamp.Match(block);

                var title = CleanText(name.Success ? name.Groups[1].Value : string.Empty);

                if (!link.Success || title.Length == 0)
                    continue;

                var lastPost = stamp.Success && long.TryParse(stamp.Groups[1].Value, out var seconds)
                    ? DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime()
                    : (DateTimeOffset?)null;

                topics.Add(new GroupDiscussionSnapshot(
                    title,
                    WebUtility.HtmlDecode(link.Groups[1].Value),
                    CleanText(author.Success ? author.Groups[1].Value : string.Empty),
                    replies.Success ? int.Parse(replies.Groups[1].Value, CultureInfo.InvariantCulture) : 0,
                    lastPost));

                if (topics.Count >= MaxDiscussions)
                    break;
            }

            return topics;
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    public async Task<bool> JoinAsync(string groupId, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string?>
        {
            ["action"] = "join"
        };

        await _web.PostFormAsync($"https://steamcommunity.com/gid/{groupId}", null, form, true, cancellationToken);
        return true;
    }

    public async Task<bool> LeaveAsync(string groupId, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string?>
        {
            ["action"] = "leaveGroup",
            ["groupId"] = groupId
        };

        await _web.PostFormAsync("https://steamcommunity.com/my/home_process", null, form, true, cancellationToken);
        return true;
    }

    private async Task<GroupSnapshot?> TryGetByGidAsync(string gid, CancellationToken cancellationToken)
    {
        try
        {
            var xml = await _web.GetCommunityXmlAsync($"https://steamcommunity.com/gid/{gid}/memberslistxml/?xml=1", cancellationToken);
            return ParseMembersList(xml);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static GroupSnapshot? ParseMembersList(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return null;

        try
        {
            var document = XDocument.Parse(xml);
            var root = document.Root;

            if (root == null || root.Name != "memberList")
                return null;

            var details = root.Element("groupDetails");
            var groupId = TrimValue(root.Element("groupID64")?.Value);

            if (details == null || groupId.Length == 0)
                return null;

            return new GroupSnapshot(
                groupId,
                TrimValue(details.Element("groupURL")?.Value),
                TrimValue(details.Element("groupName")?.Value),
                TrimValue(details.Element("headline")?.Value),
                TrimValue(details.Element("summary")?.Value),
                TrimValue(details.Element("avatarFull")?.Value ?? details.Element("avatarMedium")?.Value ?? details.Element("avatarIcon")?.Value),
                ToInt(details.Element("memberCount")?.Value),
                ToInt(details.Element("membersOnline")?.Value),
                ToInt(details.Element("membersInGame")?.Value));
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static string GroupUrl(GroupSnapshot group, string tail) =>
        group.ShortName.Length > 0
            ? $"https://steamcommunity.com/groups/{Uri.EscapeDataString(group.ShortName)}/{tail}"
            : $"https://steamcommunity.com/gid/{group.GroupId}/{tail}";

    private static string CleanText(string source) =>
        Spaces.Replace(WebUtility.HtmlDecode(source), " ").Trim();

    private static string ExtractShortName(string query)
    {
        var text = query.Trim();

        if (text.Length == 0)
            return string.Empty;

        var marker = "/groups/";

        if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
        {
            var start = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase) + marker.Length;
            var tail = text[start..];
            var end = tail.IndexOfAny(['/', '?', '#']);

            text = end < 0 ? tail : tail[..end];
        }

        return text.Trim().TrimEnd('/');
    }

    private static string TrimValue(string? value) => value?.Trim() ?? string.Empty;

    private static int ToInt(string? value) =>
        int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
}

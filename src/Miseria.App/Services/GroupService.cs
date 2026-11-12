using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Miseria.Core;

namespace Miseria.App.Services;

public sealed record GroupSnapshot(string GroupId, string ShortName, string Name, string Headline, string Summary, string? AvatarUrl, int MemberCount, int OnlineCount, int InGameCount);

public sealed class GroupService
{
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

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace Miseria.Core;

public sealed class SteamWebApi
{
    private const string ApiBase = "https://api.steampowered.com";
    private const string CommunityBase = "https://steamcommunity.com";

    private readonly Func<Task<WebIdentity>> _identity;
    private readonly HttpClient _http;

    public SteamWebApi(Func<Task<WebIdentity>> identity)
        : this(identity, new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
    {
    }

    public SteamWebApi(Func<Task<WebIdentity>> identity, HttpMessageHandler handler)
    {
        _identity = identity;
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; Miseria/1.0)");
    }

    public async Task<JsonNode?> GetApiJsonAsync(string path, IDictionary<string, string?> args, CancellationToken cancellationToken = default)
    {
        var identity = await _identity();
        var url = BuildUrl($"{ApiBase}/{path}", Merge(args, new Dictionary<string, string?> { ["access_token"] = identity.AccessToken }));
        return await GetJsonAsync(url, cookies: null, cancellationToken);
    }

    public async Task<JsonNode?> GetPublicJsonAsync(string url, CancellationToken cancellationToken = default)
        => await GetJsonAsync(url, cookies: null, cancellationToken);

    public async Task<JsonNode?> GetPublicCommunityJsonAsync(string path, IDictionary<string, string?> args, CancellationToken cancellationToken = default)
    {
        var url = BuildUrl($"{CommunityBase}/{path}", args);
        return await GetJsonAsync(url, cookies: null, cancellationToken);
    }

    public async Task<JsonNode?> GetCommunityJsonAsync(string path, IDictionary<string, string?> args, CancellationToken cancellationToken = default)
    {
        var cookies = await BuildCookiesAsync();
        var url = BuildUrl($"{CommunityBase}/{path}", args);
        return await GetJsonAsync(url, cookies, cancellationToken);
    }

    public async Task<string?> GetCommunityTextAsync(string url, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Referrer = new Uri(CommunityBase);

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<JsonNode?> PostCommunityFormAsync(string path, IDictionary<string, string?> form, CancellationToken cancellationToken = default)
    {
        var identity = await _identity();
        var fields = new Dictionary<string, string?> { ["sessionid"] = identity.SessionId };
        foreach (var pair in form)
            fields[pair.Key] = pair.Value;

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl($"{CommunityBase}/{path}", null))
        {
            Content = new FormUrlEncodedContent(fields.Where(f => f.Value != null)
                .Select(f => new KeyValuePair<string, string>(f.Key, f.Value!)))
        };
        request.Headers.Referrer = new Uri(CommunityBase);
        request.Headers.TryAddWithoutValidation("X-Requested-With", "Steam App Version 3");
        SetCookies(request, await BuildCookiesAsync());

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return TryParse(text);
    }

    private static JsonNode? TryParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            return JsonNode.Parse(text);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private async Task<string> BuildCookiesAsync()
    {
        var identity = await _identity();
        var token = Uri.EscapeDataString(identity.AccessToken);
        return $"steamLoginSecure={identity.SteamId}||{token}; sessionid={identity.SessionId}";
    }

    private async Task<JsonNode?> GetJsonAsync(string url, string? cookies, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        if (cookies != null)
            SetCookies(request, cookies);

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    private static void SetCookies(HttpRequestMessage request, string cookies)
    {
        request.Headers.TryAddWithoutValidation("Cookie", cookies);
    }

    private static string BuildUrl(string baseUrl, IDictionary<string, string?>? args)
    {
        if (args == null || args.Count == 0)
            return baseUrl;

        var query = string.Join("&", args.Where(a => a.Value != null)
            .Select(a => $"{Uri.EscapeDataString(a.Key)}={Uri.EscapeDataString(a.Value!)}"));

        return $"{baseUrl}?{query}";
    }

    private static IDictionary<string, string?> Merge(IDictionary<string, string?> source, IDictionary<string, string?> extra)
    {
        var merged = new Dictionary<string, string?>(source);

        foreach (var pair in extra)
            merged[pair.Key] = pair.Value;

        return merged;
    }
}

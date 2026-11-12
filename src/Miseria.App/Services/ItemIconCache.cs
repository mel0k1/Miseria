using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Avalonia.Media.Imaging;

namespace Miseria.App.Services;

public sealed class ItemIconCache : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly ConcurrentDictionary<string, Task<Bitmap?>> _memory = [];

    private static string CacheDirectory
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Miseria",
                "items");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    public Task<Bitmap?> LoadAsync(string? iconUrl)
    {
        if (string.IsNullOrWhiteSpace(iconUrl))
            return Task.FromResult<Bitmap?>(null);

        return _memory.GetOrAdd($"eco/{iconUrl}", _ => LoadEconomyAsync(iconUrl));
    }

    public Task<Bitmap?> LoadUrlAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Task.FromResult<Bitmap?>(null);

        return _memory.GetOrAdd($"url/{url}", _ => LoadUrlCoreAsync(url));
    }

    private async Task<Bitmap?> LoadEconomyAsync(string iconUrl)
    {
        return await LoadUrlCoreAsync($"https://community.cloudflare.steamstatic.com/economy/image/{iconUrl}/96fx96f");
    }

    private async Task<Bitmap?> LoadUrlCoreAsync(string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        var file = Path.Combine(CacheDirectory, hash[..40] + ".img");

        try
        {
            if (!File.Exists(file))
            {
                using var response = await _http.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return null;

                await using var source = await response.Content.ReadAsStreamAsync();
                await using var target = File.Create(file);
                await source.CopyToAsync(target);
            }

            return new Bitmap(file);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}

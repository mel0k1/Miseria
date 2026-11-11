using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using Avalonia.Media.Imaging;

namespace Miseria.App.Services;

public sealed class AppIconCache : IDisposable
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
                "appicons");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    public Task<Bitmap?> LoadAsync(uint appId, string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash))
            return Task.FromResult<Bitmap?>(null);

        return _memory.GetOrAdd(appId + ":" + hash, _ => LoadCoreAsync(appId, hash));
    }

    private async Task<Bitmap?> LoadCoreAsync(uint appId, string hash)
    {
        var file = Path.Combine(CacheDirectory, appId + "_" + hash + ".jpg");

        try
        {
            if (!File.Exists(file))
            {
                using var response = await _http.GetAsync($"https://media.steampowered.com/steamcommunity/public/images/apps/{appId}/{hash}.jpg");

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

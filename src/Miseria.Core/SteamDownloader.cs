using System.Diagnostics;
using System.IO;
using SteamKit2;
using SteamKit2.CDN;

namespace Miseria.Core;

public sealed record DownloadStart(uint AppId, string AppName, ulong TotalBytes, int FileCount, string DestinationPath);

public sealed record DownloadProgress(uint AppId, string FileName, ulong DoneBytes, ulong TotalBytes, double BytesPerSecond);

public sealed record DownloadFinish(uint AppId, ulong TotalBytes, DownloadFinishKind Kind, string Message);

public enum DownloadFinishKind
{
    Completed,
    Cancelled,
    Failed
}

public sealed class SteamDownloader : IDisposable
{
    private sealed record Depot(uint DepotId, ulong ManifestId, byte[] DepotKey);

    private sealed record PreparedFile(uint DepotId, byte[] DepotKey, string FileName, ulong TotalSize, string? LinkTarget, IReadOnlyList<DepotManifest.ChunkData> Chunks);

    private readonly SteamClient _client;
    private readonly SteamApps _apps;
    private readonly SteamContent _content;

    public event Action<DownloadStart>? Started;
    public event Action<DownloadProgress>? Progress;
    public event Action<DownloadFinish>? Finished;

    public SteamDownloader(SteamClient client, SteamApps apps, SteamContent content)
    {
        _client = client;
        _apps = apps;
        _content = content;
    }

    public async Task DownloadAsync(uint appId, string destination, CancellationToken cancellationToken)
    {
        try
        {
            var (files, appName) = await PrepareAsync(appId, cancellationToken);

            if (files.Count == 0)
                throw new InvalidOperationException("Нет доступных для загрузки файлов. Возможно, приложение не приобретено.");

            var totalBytes = files.Aggregate(0UL, (sum, f) => sum + f.TotalSize);
            var destinationPath = Path.GetFullPath(destination);
            Directory.CreateDirectory(destinationPath);

            Started?.Invoke(new DownloadStart(appId, appName, totalBytes, files.Count, destinationPath));

            var doneBytes = 0UL;
            var stopwatch = Stopwatch.StartNew();
            var lastBytes = 0UL;
            var lastTime = TimeSpan.Zero;
            var speed = 0.0;
            var servers = await GetServersAsync(cancellationToken);

            foreach (var (file, index) in files.Select((f, i) => (f, i)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (file.LinkTarget != null)
                {
                    CreateLink(Path.Combine(destinationPath, file.FileName), file.LinkTarget);
                    continue;
                }

                var targetPath = Path.Combine(destinationPath, file.FileName);
                var directory = Path.GetDirectoryName(targetPath);

                if (directory != null)
                    Directory.CreateDirectory(directory);

                if (File.Exists(targetPath) && new FileInfo(targetPath).Length == (long)file.TotalSize)
                {
                    doneBytes += file.TotalSize;
                    continue;
                }

                var server = servers[index % servers.Length];
                var token = await AuthorizeAsync(appId, file.DepotId, server, cancellationToken);

                await using var stream = File.Create(targetPath);

                foreach (var batch in file.Chunks.Chunk(4))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var buffers = batch
                        .Select(c => new byte[file.DepotKey != null ? Math.Max(c.UncompressedLength, 1) : Math.Max(c.CompressedLength, 1)])
                        .ToArray();
                    var written = await Task.WhenAll(batch.Select((chunk, i) =>
                        DownloadChunkAsync(file.DepotId, chunk, server, buffers[i], file.DepotKey, token, cancellationToken)));

                    for (var i = 0; i < batch.Length; i++)
                    {
                        stream.Position = (long)batch[i].Offset;
                        await stream.WriteAsync(buffers[i].AsMemory(0, written[i]), cancellationToken);
                        doneBytes += (ulong)written[i];
                    }

                    var elapsed = stopwatch.Elapsed;

                    if (elapsed - lastTime > TimeSpan.FromMilliseconds(200))
                    {
                        var instant = (doneBytes - lastBytes) / (elapsed - lastTime).TotalSeconds;
                        speed = speed == 0 ? instant : speed * 0.6 + instant * 0.4;
                        lastBytes = doneBytes;
                        lastTime = elapsed;
                        Progress?.Invoke(new DownloadProgress(appId, file.FileName, doneBytes, totalBytes, speed));
                    }
                }

                Progress?.Invoke(new DownloadProgress(appId, file.FileName, doneBytes, totalBytes, speed));
            }

            Finished?.Invoke(new DownloadFinish(appId, totalBytes, DownloadFinishKind.Completed, string.Empty));
        }
        catch (OperationCanceledException)
        {
            Finished?.Invoke(new DownloadFinish(appId, 0, DownloadFinishKind.Cancelled, string.Empty));
        }
        catch (Exception ex)
        {
            Finished?.Invoke(new DownloadFinish(appId, 0, DownloadFinishKind.Failed, ex.Message));
        }
    }

    public void Dispose()
    {
    }

    private async Task<(List<PreparedFile> Files, string AppName)> PrepareAsync(uint appId, CancellationToken cancellationToken)
    {
        var info = await GetAppInfoAsync(appId, cancellationToken);
        var appName = info["common"]["name"].AsString() ?? appId.ToString();
        var currentOs = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";

        var depots = new List<Depot>();

        foreach (var depot in info["depots"].Children)
        {
            if (!uint.TryParse(depot.Name, out var depotId) || depotId == 0)
                continue;

            if (!string.IsNullOrEmpty(depot["dlcappid"].Value))
                continue;

            if (!string.IsNullOrEmpty(depot["depotfromapp"].Value))
                continue;

            var oslist = depot["config"]["oslist"].Value;

            if (!string.IsNullOrEmpty(oslist) && !oslist.Split(',').Contains(currentOs))
                continue;

            var language = depot["config"]["language"].Value;

            if (!string.IsNullOrEmpty(language))
                continue;

            var manifestNode = depot["manifests"]["public"];
            var manifestValue = string.IsNullOrEmpty(manifestNode.Value) ? manifestNode["gid"].Value : manifestNode.Value;

            if (!ulong.TryParse(manifestValue, out var manifestId) || manifestId == 0)
                continue;

            try
            {
                var key = await _apps.GetDepotDecryptionKey(depotId, appId);

                if (key.Result == EResult.OK)
                    depots.Add(new Depot(depotId, manifestId, key.DepotKey));
            }
            catch
            {
            }
        }

        var servers = await GetServersAsync(cancellationToken);
        var files = new List<PreparedFile>();

        foreach (var (depot, index) in depots.Select((d, i) => (d, i)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var server = servers[index % servers.Length];
            var manifest = await DownloadManifestAsync(depot, server, appId, servers);

            if (manifest?.Files == null)
                continue;

            files.AddRange(manifest.Files.Select(f => new PreparedFile(
                depot.DepotId,
                depot.DepotKey,
                NormalizePath(f.FileName),
                f.TotalSize,
                f.LinkTarget,
                f.Chunks)));
        }

        return (files, appName);
    }

    private async Task<Server[]> GetServersAsync(CancellationToken cancellationToken)
    {
        var servers = await _content.GetServersForSteamPipe();

        var usable = servers
            .Where(s => s.Host != null && s.Type is "SteamCache" or "SteamCache-Lancache" or "CS")
            .ToArray();

        if (usable.Length == 0)
            usable = servers.Where(s => s.Host != null).ToArray();

        if (usable.Length == 0)
            throw new InvalidOperationException("Не удалось получить серверы Steam CDN");

        return usable;
    }

    private async Task<KeyValue> GetAppInfoAsync(uint appId, CancellationToken cancellationToken)
    {
        ulong token = 0;

        try
        {
            var tokens = await _apps.PICSGetAccessTokens(new[] { appId }, []);

            if (!tokens.AppTokens.TryGetValue(appId, out token))
                token = 0;
        }
        catch
        {
        }

        var result = await _apps.PICSGetProductInfo(
            new[] { new SteamApps.PICSRequest(appId, token) },
            []);

        var app = result.Results?.SelectMany(r => r.Apps.Values).FirstOrDefault(a => a.ID == appId)
            ?? throw new InvalidOperationException($"Приложение {appId} не найдено в Steam");

        return app.KeyValues;
    }

    private async Task<DepotManifest> DownloadManifestAsync(Depot depot, Server server, uint appId, Server[] servers)
    {
        ulong requestCode = 0;

        try
        {
            requestCode = await _content.GetManifestRequestCode(depot.DepotId, appId, depot.ManifestId, "public", null);
        }
        catch
        {
        }

        using var client = new Client(_client);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await client.DownloadManifestAsync(depot.DepotId, depot.ManifestId, requestCode, server, depot.DepotKey, null, null);
            }
            catch when (attempt < 2)
            {
                server = servers[(Array.IndexOf(servers, server) + 1) % servers.Length];
            }
        }
    }

    private async Task<string> AuthorizeAsync(uint appId, uint depotId, Server server, CancellationToken cancellationToken)
    {
        var auth = await _content.GetCDNAuthToken(appId, depotId, server.Host!);

        if (auth.Result != EResult.OK)
            throw new InvalidOperationException($"Steam отказал в доступе к депо {depotId} ({auth.Result})");

        return auth.Token;
    }

    private async Task<int> DownloadChunkAsync(uint depotId, DepotManifest.ChunkData chunk, Server server, byte[] buffer, byte[] depotKey, string cdnAuthToken, CancellationToken cancellationToken)
    {
        using var client = new Client(_client);

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await client.DownloadDepotChunkAsync(depotId, chunk, server, buffer, depotKey, null, cdnAuthToken);
            }
            catch when (attempt < 2)
            {
            }
        }
    }

    private static string NormalizePath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Any(p => p is "." or ".."))
            throw new InvalidOperationException($"Опасный путь в манифесте: {path}");

        return Path.Combine(parts);
    }

    private static void CreateLink(string path, string target)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                return;

            if (File.Exists(path) || Directory.Exists(path))
                return;

            var directory = Path.GetDirectoryName(path);

            if (directory != null)
                Directory.CreateDirectory(directory);

            File.CreateSymbolicLink(path, target.Replace('\\', '/'));
        }
        catch
        {
        }
    }
}

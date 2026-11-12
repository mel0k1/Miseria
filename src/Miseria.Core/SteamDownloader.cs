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

public sealed record BranchInfo(string Name, bool PasswordRequired, uint BuildId, DateTime Updated);

public sealed record LaunchOption(string Executable, string Arguments, string OsList);

public sealed record ManifestFileInfo(string Path, ulong Size, string Hash);

public sealed record DepotManifestInfo(uint DepotId, ulong ManifestId, IReadOnlyList<ManifestFileInfo> Files);

public sealed record ManifestRecord(uint AppId, string Branch, IReadOnlyList<DepotManifestInfo> Depots);

public enum UpdateStatus
{
    UpToDate,
    Available,
    PasswordRequired,
    Failed
}

public sealed record UpdateCheck(uint AppId, string Name, string Branch, UpdateStatus Status, int ChangedFiles, int RemovedFiles, ulong DownloadBytes, string Message);

public sealed record DownloadResult(uint AppId, string AppName, string Branch, string DestinationPath, ulong TotalBytes, IReadOnlyList<LaunchOption> LaunchOptions, ManifestRecord Manifest);

public sealed class SteamDownloader : IDisposable
{
    private sealed record Depot(uint DepotId, ulong ManifestId, byte[] DepotKey);

    private sealed record PreparedFile(uint DepotId, byte[] DepotKey, string FileName, ulong TotalSize, string FileHash, string? LinkTarget, IReadOnlyList<DepotManifest.ChunkData> Chunks);

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

    public async Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(uint appId, CancellationToken cancellationToken)
    {
        var (info, _) = await GetAppInfoAsync(appId, cancellationToken);
        var branches = new List<BranchInfo>();

        foreach (var branch in info["depots"]["branches"].Children)
        {
            if (string.IsNullOrEmpty(branch.Name) || branch["buildid"].Value == null)
                continue;

            var updated = ulong.TryParse(branch["timeupdated"].Value, out var time) && time > 0
                ? DateTimeOffset.FromUnixTimeSeconds((long)time).LocalDateTime
                : default;

            branches.Add(new BranchInfo(
                branch.Name,
                branch["pwdrequired"].Value == "1",
                uint.TryParse(branch["buildid"].Value, out var buildId) ? buildId : 0,
                updated));
        }

        return branches;
    }

    public async Task<UpdateCheck> CheckUpdateAsync(uint appId, ManifestRecord previous, string? betaPassword, CancellationToken cancellationToken)
    {
        var appName = appId.ToString();

        try
        {
            var (info, accessToken) = await GetAppInfoAsync(appId, cancellationToken);
            appName = info["common"]["name"].AsString() ?? appName;
            var branch = previous.Branch;
            var branchNode = info["depots"]["branches"][branch];

            if (branchNode["buildid"].Value == null)
                return new UpdateCheck(appId, appName, branch, UpdateStatus.Failed, 0, 0, 0, $"Ветка «{branch}» не найдена");

            KeyValue? betaSection = null;

            if (branchNode["pwdrequired"].Value == "1")
            {
                if (string.IsNullOrWhiteSpace(betaPassword))
                    return new UpdateCheck(appId, appName, branch, UpdateStatus.PasswordRequired, 0, 0, 0, "Требуется пароль ветки");

                betaSection = await GetPrivateBetaAsync(appId, accessToken, branch, betaPassword, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var depots = await CollectDepotsAsync(appId, info, accessToken, branch, betaSection, cancellationToken);
            var old = previous.Depots.ToDictionary(d => d.DepotId);
            var changed = depots
                .Where(d => !old.TryGetValue(d.DepotId, out var known) || known.ManifestId != d.ManifestId)
                .ToList();
            var removedDepots = old.Values.Where(d => depots.All(c => c.DepotId != d.DepotId)).ToList();

            if (changed.Count == 0 && removedDepots.Count == 0)
                return new UpdateCheck(appId, appName, branch, UpdateStatus.UpToDate, 0, 0, 0, string.Empty);

            var changedFiles = 0;
            var removedFiles = 0;
            var downloadBytes = 0UL;
            var servers = await GetServersAsync(cancellationToken);

            foreach (var depot in changed)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var oldFiles = old.TryGetValue(depot.DepotId, out var known) ? known.Files : [];
                var manifest = await TryDownloadManifestAsync(depot, appId, branch, servers);

                if (manifest?.Files == null)
                {
                    changedFiles += oldFiles.Count;
                    downloadBytes += oldFiles.Aggregate(0UL, (sum, f) => sum + f.Size);
                    continue;
                }

                var currentPaths = new HashSet<string>();

                foreach (var file in manifest.Files)
                {
                    if (file.Flags.HasFlag(EDepotFileFlag.Directory))
                        continue;

                    var path = NormalizePath(file.FileName);
                    currentPaths.Add(path);

                    var knownFile = oldFiles.FirstOrDefault(f => f.Path == path);

                    if (knownFile == null || knownFile.Hash != ToHex(file.FileHash) || knownFile.Size != file.TotalSize)
                    {
                        changedFiles++;
                        downloadBytes += file.TotalSize;
                    }
                }

                removedFiles += oldFiles.Count(f => !currentPaths.Contains(f.Path));
            }

            foreach (var removed in removedDepots)
                removedFiles += removed.Files.Count;

            return new UpdateCheck(appId, appName, branch, UpdateStatus.Available, changedFiles, removedFiles, downloadBytes, string.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateCheck(appId, appName, previous.Branch, UpdateStatus.Failed, 0, 0, 0, ex.Message);
        }
    }

    public async Task<DownloadResult?> DownloadAsync(uint appId, string destination, string branch, string? betaPassword, CancellationToken cancellationToken, ManifestRecord? previous = null)
    {
        try
        {
            var (files, appName, launchOptions, manifest) = await PrepareAsync(appId, branch, betaPassword, cancellationToken);

            if (files.Count == 0)
                throw new InvalidOperationException("Нет доступных для загрузки файлов. Возможно, приложение не приобретено.");

            var destinationPath = Path.GetFullPath(destination);
            Directory.CreateDirectory(destinationPath);

            foreach (var file in files)
            {
                if (string.IsNullOrEmpty(file.LinkTarget))
                    continue;

                CreateLink(Path.Combine(destinationPath, file.FileName), file.LinkTarget);
            }

            var plan = BuildPlan(files, previous, destinationPath);

            Started?.Invoke(new DownloadStart(appId, appName, plan.TotalBytes, plan.ToDownload.Count, destinationPath));

            var doneBytes = 0UL;
            var stopwatch = Stopwatch.StartNew();
            var lastBytes = 0UL;
            var lastTime = TimeSpan.Zero;
            var speed = 0.0;
            var servers = await GetServersAsync(cancellationToken);

            foreach (var (file, index) in plan.ToDownload.Select((f, i) => (f, i)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var targetPath = Path.Combine(destinationPath, file.FileName);
                var directory = Path.GetDirectoryName(targetPath);

                if (directory != null)
                    Directory.CreateDirectory(directory);

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
                        DownloadChunkAsync(file.DepotId, chunk, server, servers, buffers[i], file.DepotKey, token, cancellationToken)));

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
                        Progress?.Invoke(new DownloadProgress(appId, file.FileName, doneBytes, plan.TotalBytes, speed));
                    }
                }

                Progress?.Invoke(new DownloadProgress(appId, file.FileName, doneBytes, plan.TotalBytes, speed));
            }

            if (previous != null)
                DeleteRemovedFiles(previous, files, destinationPath);

            Finished?.Invoke(new DownloadFinish(appId, plan.TotalBytes, DownloadFinishKind.Completed, string.Empty));

            var totalSize = files.Aggregate(0UL, (sum, f) => sum + f.TotalSize);
            return new DownloadResult(appId, appName, branch, destinationPath, totalSize, launchOptions, manifest);
        }
        catch (OperationCanceledException)
        {
            Finished?.Invoke(new DownloadFinish(appId, 0, DownloadFinishKind.Cancelled, string.Empty));
            return null;
        }
        catch (Exception ex)
        {
            Finished?.Invoke(new DownloadFinish(appId, 0, DownloadFinishKind.Failed, ex.Message));
            return null;
        }
    }

    public void Dispose()
    {
    }

    private static (List<PreparedFile> ToDownload, ulong TotalBytes) BuildPlan(List<PreparedFile> files, ManifestRecord? previous, string destinationPath)
    {
        var oldFiles = previous == null
            ? []
            : previous.Depots.ToDictionary(d => d.DepotId, d => d.Files.ToDictionary(f => f.Path, StringComparer.Ordinal));
        var toDownload = new List<PreparedFile>();

        foreach (var file in files)
        {
            if (!string.IsNullOrEmpty(file.LinkTarget))
                continue;

            var targetPath = Path.Combine(destinationPath, file.FileName);

            if (File.Exists(targetPath) && new FileInfo(targetPath).Length == (long)file.TotalSize)
            {
                if (previous == null)
                    continue;

                if (oldFiles.TryGetValue(file.DepotId, out var map)
                    && map.TryGetValue(file.FileName, out var known)
                    && known.Hash == file.FileHash
                    && known.Size == file.TotalSize)
                    continue;
            }

            toDownload.Add(file);
        }

        return (toDownload, toDownload.Aggregate(0UL, (sum, f) => sum + f.TotalSize));
    }

    private static void DeleteRemovedFiles(ManifestRecord previous, List<PreparedFile> files, string destinationPath)
    {
        var current = files.Select(f => (Depot: f.DepotId, Path: f.FileName)).ToHashSet();

        foreach (var depot in previous.Depots)
        {
            foreach (var file in depot.Files)
            {
                if (current.Contains((depot.DepotId, file.Path)))
                    continue;

                try
                {
                    File.Delete(Path.Combine(destinationPath, file.Path));
                }
                catch
                {
                }
            }
        }
    }

    private async Task<(List<PreparedFile> Files, string AppName, IReadOnlyList<LaunchOption> LaunchOptions, ManifestRecord Manifest)> PrepareAsync(uint appId, string branch, string? betaPassword, CancellationToken cancellationToken)
    {
        var (info, accessToken) = await GetAppInfoAsync(appId, cancellationToken);
        var appName = info["common"]["name"].AsString() ?? appId.ToString();
        var currentOs = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";

        var branchNode = info["depots"]["branches"][branch];

        if (branchNode["buildid"].Value == null)
            throw new InvalidOperationException($"Ветка «{branch}» не найдена");

        KeyValue? betaSection = null;

        if (branchNode["pwdrequired"].Value == "1")
        {
            if (string.IsNullOrWhiteSpace(betaPassword))
                throw new InvalidOperationException($"Ветка «{branch}» защищена паролем. Введите пароль ветки.");

            betaSection = await GetPrivateBetaAsync(appId, accessToken, branch, betaPassword, cancellationToken);
        }

        var depots = await CollectDepotsAsync(appId, info, accessToken, branch, betaSection, cancellationToken);
        var servers = await GetServersAsync(cancellationToken);
        var files = new List<PreparedFile>();

        foreach (var (depot, index) in depots.Select((d, i) => (d, i)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var server = servers[index % servers.Length];
            var manifest = await TryDownloadManifestAsync(depot, appId, branch, servers);

            if (manifest?.Files == null)
                continue;

            files.AddRange(manifest.Files
                .Where(f => !f.Flags.HasFlag(EDepotFileFlag.Directory))
                .Select(f => new PreparedFile(
                    depot.DepotId,
                    depot.DepotKey,
                    NormalizePath(f.FileName),
                    f.TotalSize,
                    ToHex(f.FileHash),
                    f.LinkTarget,
                    f.Chunks)));
        }

        var manifestDepots = new List<DepotManifestInfo>();

        foreach (var group in files.GroupBy(f => f.DepotId))
        {
            var gid = depots.First(d => d.DepotId == group.Key).ManifestId;
            manifestDepots.Add(new DepotManifestInfo(group.Key, gid, group
                .Select(f => new ManifestFileInfo(f.FileName, f.TotalSize, f.FileHash))
                .ToList()));
        }

        return (files, appName, GetLaunchOptions(info), new ManifestRecord(appId, branch, manifestDepots));
    }

    private async Task<List<Depot>> CollectDepotsAsync(uint appId, KeyValue info, ulong accessToken, string branch, KeyValue? betaSection, CancellationToken cancellationToken)
    {
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

            var manifestNode = depot["manifests"][branch];
            var manifestValue = string.IsNullOrEmpty(manifestNode.Value) ? manifestNode["gid"].Value : manifestNode.Value;

            if (!ulong.TryParse(manifestValue, out var manifestId) || manifestId == 0)
                manifestValue = betaSection?[depotId.ToString()]["manifests"][branch]["gid"].Value;

            if (!ulong.TryParse(manifestValue, out manifestId) || manifestId == 0)
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

        return depots;
    }

    private async Task<KeyValue> GetPrivateBetaAsync(uint appId, ulong accessToken, string branch, string password, CancellationToken cancellationToken)
    {
        var check = await _apps.CheckAppBetaPassword(appId, password);

        if (check.Result != EResult.OK || !check.BetaPasswords.TryGetValue(branch, out var key))
            throw new InvalidOperationException("Steam не принял пароль ветки");

        var beta = await _apps.PICSGetPrivateBeta(appId, accessToken, branch, key);

        if (beta.Result != EResult.OK || beta.DepotSection.Children.Count == 0)
            throw new InvalidOperationException("Steam отклонил доступ к закрытой ветке");

        return beta.DepotSection;
    }

    private static List<LaunchOption> GetLaunchOptions(KeyValue info)
    {
        var options = new List<LaunchOption>();

        foreach (var entry in info["config"]["launch"].Children)
        {
            var executable = entry["executable"].Value;

            if (string.IsNullOrWhiteSpace(executable))
                continue;

            options.Add(new LaunchOption(
                executable,
                entry["arguments"].Value ?? string.Empty,
                entry["config"]["oslist"].Value ?? string.Empty));
        }

        return options;
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

    private async Task<(KeyValue Info, ulong AccessToken)> GetAppInfoAsync(uint appId, CancellationToken cancellationToken)
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

        return (app.KeyValues, token);
    }

    private async Task<DepotManifest?> TryDownloadManifestAsync(Depot depot, uint appId, string branch, Server[] servers)
    {
        try
        {
            return await DownloadManifestAsync(depot, servers[0], appId, branch, servers);
        }
        catch
        {
            return null;
        }
    }

    private async Task<DepotManifest> DownloadManifestAsync(Depot depot, Server server, uint appId, string branch, Server[] servers)
    {
        ulong requestCode = 0;

        try
        {
            requestCode = await _content.GetManifestRequestCode(depot.DepotId, appId, depot.ManifestId, branch, null);
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

    private async Task<string?> AuthorizeAsync(uint appId, uint depotId, Server server, CancellationToken cancellationToken)
    {
        try
        {
            var auth = await _content.GetCDNAuthToken(appId, depotId, server.Host!);

            if (auth.Result == EResult.OK)
                return auth.Token;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }

        return null;
    }

    private async Task<int> DownloadChunkAsync(uint depotId, DepotManifest.ChunkData chunk, Server server, Server[] servers, byte[] buffer, byte[] depotKey, string? cdnAuthToken, CancellationToken cancellationToken)
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
                server = servers[(Array.IndexOf(servers, server) + 1) % servers.Length];
            }
        }
    }

    private static string ToHex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();

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

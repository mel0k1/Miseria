using System.IO;
using System.Text.Json;
using Miseria.Core;

namespace Miseria.App.Services;

public sealed class ManifestStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public void Save(ManifestRecord record)
    {
        try
        {
            File.WriteAllText(FilePath(record.AppId), JsonSerializer.Serialize(record, Options));
        }
        catch
        {
        }
    }

    public ManifestRecord? TryGet(uint appId)
    {
        try
        {
            var path = FilePath(appId);

            return File.Exists(path)
                ? JsonSerializer.Deserialize<ManifestRecord>(File.ReadAllText(path), Options)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string FilePath(uint appId)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Miseria",
            "manifests");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"appmanifest_{appId}.json");
    }
}

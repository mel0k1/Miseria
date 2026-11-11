using System.IO;
using System.Text.Json;

namespace Miseria.App.Services;

public sealed record InstalledApp(uint AppId, string Name, string Branch, string Directory, ulong TotalBytes, DateTime InstalledAt, string? Executable, string? Arguments);

public sealed class InstallStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly List<InstalledApp> _installs;

    public event Action? Changed;

    public InstallStore()
    {
        _installs = Load();
    }

    public IReadOnlyList<InstalledApp> Installs => _installs;

    public bool TryGet(uint appId, out InstalledApp install)
    {
        var found = _installs.FirstOrDefault(i => i.AppId == appId);

        if (found == null)
        {
            install = new InstalledApp(0, string.Empty, string.Empty, string.Empty, 0, default, null, null);
            return false;
        }

        install = found;
        return true;
    }

    public void Add(InstalledApp install)
    {
        _installs.RemoveAll(i => i.AppId == install.AppId);
        _installs.Add(install);
        Save();
        Changed?.Invoke();
    }

    public void Remove(uint appId)
    {
        if (_installs.RemoveAll(i => i.AppId == appId) == 0)
            return;

        Save();
        Changed?.Invoke();
    }

    private static string FilePath
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Miseria");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "installs.json");
        }
    }

    private List<InstalledApp> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];

            return JsonSerializer.Deserialize<List<InstalledApp>>(File.ReadAllText(FilePath), Options) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_installs, Options));
        }
        catch
        {
        }
    }
}

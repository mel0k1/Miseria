using System.IO;
using System.Text.Json;

namespace Miseria.Core;

public sealed record StoredSession(string AccountName, ulong SteamId, string RefreshToken, string? GuardData, DateTimeOffset SavedAtUtc);

public sealed class SessionStore
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Miseria");

    private static string FilePath => Path.Combine(DirectoryPath, "session.json");

    public StoredSession? TryLoad()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;

            return JsonSerializer.Deserialize<StoredSession>(File.ReadAllText(FilePath));
        }
        catch
        {
            return null;
        }
    }

    public void Save(StoredSession session)
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(session));

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            try
            {
                File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch
            {
            }
        }
    }

    public void Delete()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch
        {
        }
    }
}

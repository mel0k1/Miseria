using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Miseria.App.Theming;

namespace Miseria.App.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string FilePath
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Miseria");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "settings.json");
        }
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
    }
}

public sealed record AppSettings(AppTheme Theme = AppTheme.Light);

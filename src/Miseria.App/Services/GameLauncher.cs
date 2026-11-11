using System.Diagnostics;
using System.IO;
using Miseria.Core;

namespace Miseria.App.Services;

public static class GameLauncher
{
    public static LaunchOption? Pick(IReadOnlyList<LaunchOption> options)
    {
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";

        return options.FirstOrDefault(o => o.OsList
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains(os))
            ?? options.FirstOrDefault(o => o.OsList.Length == 0);
    }

    public static void Launch(InstalledApp install)
    {
        if (string.IsNullOrWhiteSpace(install.Executable))
            throw new InvalidOperationException("У приложения нет настроек запуска");

        var executable = install.Executable.Replace('\\', '/');
        var fullPath = Path.Combine(install.Directory, executable);

        if (!File.Exists(fullPath))
            throw new InvalidOperationException($"Файл не найден: {executable}");

        var start = new ProcessStartInfo
        {
            WorkingDirectory = Path.GetDirectoryName(fullPath) ?? install.Directory,
            Arguments = install.Arguments ?? string.Empty,
            UseShellExecute = true
        };

        if (!OperatingSystem.IsWindows() && fullPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            var wine = FindWine();

            if (wine == null)
                throw new InvalidOperationException("Для запуска Windows-игр нужен Wine или Proton");

            start.FileName = wine;
            start.Arguments = string.IsNullOrEmpty(start.Arguments)
                ? $"\"{fullPath}\""
                : $"\"{fullPath}\" {start.Arguments}";
        }
        else
        {
            start.FileName = fullPath;
        }

        Process.Start(start);
    }

    private static string? FindWine()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "wine");

            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}

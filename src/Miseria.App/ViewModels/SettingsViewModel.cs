using CommunityToolkit.Mvvm.ComponentModel;
using Miseria.App.Services;
using Miseria.App.Theming;

namespace Miseria.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ThemeService _themeService;
    private readonly SettingsStore _store;

    public IReadOnlyList<ThemeOption> Themes { get; } =
    [
        new(AppTheme.Light, "Светлая", "Стандартная светлая тема Fluent"),
        new(AppTheme.Dark, "Тёмная", "Стандартная тёмная тема Fluent"),
        new(AppTheme.Crimson, "Чёрно-красная", "Глубокий чёрный фон и красный акцент"),
        new(AppTheme.Kiwi, "Киви", "Тёплый коричневый фон и зелёный акцент спелого киви")
    ];

    [ObservableProperty]
    private ThemeOption? _selectedTheme;

    public SettingsViewModel(ThemeService themeService, SettingsStore store)
    {
        _themeService = themeService;
        _store = store;
        _selectedTheme = Themes.FirstOrDefault(t => t.Theme == themeService.Current);
    }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value == null)
            return;

        _themeService.Apply(value.Theme);
        _store.Save(new AppSettings(value.Theme));
    }
}

public sealed record ThemeOption(AppTheme Theme, string Title, string Description);

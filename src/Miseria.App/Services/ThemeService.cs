using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using FluentAvalonia.Styling;
using Miseria.App.Theming;

namespace Miseria.App.Services;

public sealed class ThemeService(SettingsStore store)
{
    private readonly SettingsStore _store = store;
    private FluentAvaloniaTheme? _theme;
    private ResourceDictionary? _palette;

    public AppTheme Current { get; private set; } = AppTheme.Light;

    public void ApplySavedTheme() => Apply(_store.Load().Theme);

    public void Apply(AppTheme theme)
    {
        var app = Application.Current ?? throw new InvalidOperationException("Application is not initialized");
        _theme ??= app.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault()
            ?? throw new InvalidOperationException("FluentAvaloniaTheme is not registered");

        _theme.PreferSystemTheme = false;
        _theme.CustomAccentColor = theme switch
        {
            AppTheme.Crimson => Color.Parse("#E81123"),
            AppTheme.Kiwi => Color.Parse("#7CB518"),
            _ => null
        };

        app.RequestedThemeVariant = theme == AppTheme.Light ? ThemeVariant.Light : ThemeVariant.Dark;

        if (_palette != null)
            app.Resources.MergedDictionaries.Remove(_palette);

        _palette = theme switch
        {
            AppTheme.Crimson => new Crimson(),
            AppTheme.Kiwi => new Kiwi(),
            _ => null
        };

        if (_palette != null)
            app.Resources.MergedDictionaries.Add(_palette);

        Current = theme;
    }
}

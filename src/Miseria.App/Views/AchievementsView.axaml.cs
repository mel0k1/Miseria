using Avalonia.Controls;
using Avalonia.Interactivity;
using Miseria.App.ViewModels;

namespace Miseria.App.Views;

public partial class AchievementsView : UserControl
{
    public AchievementsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AchievementsViewModel vm)
            await vm.EnsureLoadedAsync();
    }
}

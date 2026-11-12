using Avalonia.Controls;
using Avalonia.Interactivity;
using Miseria.App.ViewModels;

namespace Miseria.App.Views;

public partial class FeedView : UserControl
{
    public FeedView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FeedViewModel vm)
            await vm.EnsureLoadedAsync();
    }
}

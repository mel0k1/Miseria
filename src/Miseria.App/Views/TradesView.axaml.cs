using Avalonia.Controls;
using Avalonia.Interactivity;
using Miseria.App.ViewModels;

namespace Miseria.App.Views;

public partial class TradesView : UserControl
{
    public TradesView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TradesViewModel vm)
            await vm.EnsureLoadedAsync();
    }
}

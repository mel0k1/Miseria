using Avalonia.Controls;
using Avalonia.Input;
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

    private void OnTradeTileTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border { DataContext: InventoryItem item })
            item.IsSelected = !item.IsSelected;
    }
}

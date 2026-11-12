using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Miseria.App.ViewModels;

namespace Miseria.App.Views;

public partial class MarketView : UserControl
{
    public MarketView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MarketViewModel vm)
            await vm.EnsureLoadedAsync();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MarketViewModel vm)
        {
            vm.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border { DataContext: MarketItem item } && DataContext is MarketViewModel vm)
            vm.SelectedItem = item;
    }
}

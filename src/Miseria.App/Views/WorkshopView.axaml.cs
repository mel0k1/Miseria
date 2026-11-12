using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Miseria.App.ViewModels;

namespace Miseria.App.Views;

public partial class WorkshopView : UserControl
{
    public WorkshopView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkshopViewModel vm)
            await vm.EnsureLoadedAsync();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is WorkshopViewModel vm)
        {
            vm.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border { DataContext: WorkshopItem item } && DataContext is WorkshopViewModel vm)
            vm.SelectedItem = item;
    }
}

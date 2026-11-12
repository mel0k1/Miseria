using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Miseria.App.ViewModels;

namespace Miseria.App.Views;

public partial class InventoryView : UserControl
{
    public InventoryView()
    {
        InitializeComponent();
    }

    private void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border { DataContext: InventoryItem item }
            && DataContext is InventoryViewModel viewModel)
            viewModel.SelectedItem = item;
    }
}

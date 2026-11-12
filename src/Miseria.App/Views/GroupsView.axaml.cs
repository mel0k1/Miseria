using Avalonia.Controls;
using Avalonia.Interactivity;
using Miseria.App.ViewModels;

namespace Miseria.App.Views;

public partial class GroupsView : UserControl
{
    public GroupsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GroupsViewModel vm)
            await vm.EnsureLoadedAsync();
    }

    private void OnItemTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (sender is Border { DataContext: GroupItem item } && DataContext is GroupsViewModel vm)
            vm.SelectedGroup = item;
    }
}

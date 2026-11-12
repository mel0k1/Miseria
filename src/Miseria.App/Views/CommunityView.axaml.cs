using Avalonia.Controls;
using Avalonia.Interactivity;
using Miseria.App.ViewModels;

namespace Miseria.App.Views;

public partial class CommunityView : UserControl
{
    public CommunityView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CommunityViewModel vm)
            await vm.EnsureLoadedAsync();
    }
}

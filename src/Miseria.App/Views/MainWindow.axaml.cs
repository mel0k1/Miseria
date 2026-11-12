using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Miseria.App.ViewModels;

namespace Miseria.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public MainWindow(MainViewModel viewModel) : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        viewModel.Download.Started += OnDownloadStarted;
    }

    public MainViewModel ViewModel { get; private set; } = null!;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (Nav.SelectedItem == null && Nav.MenuItems.Count > 0)
            Nav.SelectedItem = Nav.MenuItems[0];
    }

    private void OnDownloadStarted()
    {
        Dispatcher.UIThread.Post(() => NavigateTo("downloads"));
    }

    public void NavigateTo(string tag)
    {
        foreach (var item in Nav.MenuItems.OfType<FANavigationViewItem>())
        {
            if (item.Tag as string == tag)
            {
                Nav.SelectedItem = item;
                return;
            }
        }
    }

    private void OnNavSelectionChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItemContainer is FANavigationViewItem { Tag: string tag })
            ViewModel.Navigate(tag);
    }
}

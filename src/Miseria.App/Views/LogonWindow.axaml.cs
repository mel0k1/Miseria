using Avalonia.Controls;
using Miseria.App.ViewModels;

namespace Miseria.App.Views;

public partial class LogonWindow : Window
{
    public LogonWindow()
    {
        InitializeComponent();
    }

    public LogonWindow(LogonViewModel viewModel) : this()
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public LogonViewModel ViewModel { get; private set; } = null!;
}

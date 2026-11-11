using CommunityToolkit.Mvvm.ComponentModel;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public HomeViewModel Home { get; }

    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private ObservableObject? _currentPage;

    public MainViewModel(HomeViewModel home, SettingsViewModel settings)
    {
        Home = home;
        Settings = settings;
        _currentPage = home;
    }

    public void Initialize(AccountSnapshot account) => Home.Initialize(account);

    public void Navigate(string tag)
    {
        CurrentPage = tag switch
        {
            "settings" => Settings,
            _ => Home
        };
    }
}

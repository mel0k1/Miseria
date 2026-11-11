using CommunityToolkit.Mvvm.ComponentModel;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public FriendsViewModel Friends { get; }

    public HomeViewModel Home { get; }

    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private ObservableObject? _currentPage;

    public MainViewModel(FriendsViewModel friends, HomeViewModel home, SettingsViewModel settings)
    {
        Friends = friends;
        Home = home;
        Settings = settings;
        _currentPage = friends;
    }

    public void Initialize(AccountSnapshot account) => Home.Initialize(account);

    public void Navigate(string tag)
    {
        CurrentPage = tag switch
        {
            "settings" => Settings,
            "home" => Home,
            _ => Friends
        };
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public FriendsViewModel Friends { get; }

    public HomeViewModel Home { get; }

    public LibraryViewModel Library { get; }

    public DownloadViewModel Download { get; }

    public MarketViewModel Market { get; }

    public InventoryViewModel Inventory { get; }

    public TradesViewModel Trades { get; }

    public WorkshopViewModel Workshop { get; }

    public CommunityViewModel Community { get; }

    public ProfileViewModel Profile { get; }

    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private ObservableObject? _currentPage;

    public MainViewModel(FriendsViewModel friends, HomeViewModel home, LibraryViewModel library, DownloadViewModel download, MarketViewModel market, InventoryViewModel inventory, TradesViewModel trades, WorkshopViewModel workshop, CommunityViewModel community, ProfileViewModel profile, SettingsViewModel settings)
    {
        Friends = friends;
        Home = home;
        Library = library;
        Download = download;
        Market = market;
        Inventory = inventory;
        Trades = trades;
        Workshop = workshop;
        Community = community;
        Profile = profile;
        Settings = settings;
        _currentPage = friends;
    }

    public void Initialize(AccountSnapshot account)
    {
        Home.Initialize(account);
        Profile.Initialize(account);
    }

    public void Navigate(string tag)
    {
        CurrentPage = tag switch
        {
            "settings" => Settings,
            "home" => Home,
            "library" => Library,
            "downloads" => Download,
            "market" => Market,
            "inventory" => Inventory,
            "trades" => Trades,
            "workshop" => Workshop,
            "community" => Community,
            "profile" => Profile,
            _ => Friends
        };
    }
}

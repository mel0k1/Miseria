using CommunityToolkit.Mvvm.ComponentModel;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public FriendsViewModel Friends { get; }

    public FeedViewModel Feed { get; }

    public HomeViewModel Home { get; }

    public LibraryViewModel Library { get; }

    public AchievementsViewModel Achievements { get; }

    public DownloadViewModel Download { get; }

    public MarketViewModel Market { get; }

    public InventoryViewModel Inventory { get; }

    public TradesViewModel Trades { get; }

    public WorkshopViewModel Workshop { get; }

    public GroupsViewModel Groups { get; }

    public CommunityViewModel Community { get; }

    public ProfileViewModel Profile { get; }

    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private ObservableObject? _currentPage;

    public MainViewModel(FriendsViewModel friends, FeedViewModel feed, HomeViewModel home, LibraryViewModel library, AchievementsViewModel achievements, DownloadViewModel download, MarketViewModel market, InventoryViewModel inventory, TradesViewModel trades, WorkshopViewModel workshop, GroupsViewModel groups, CommunityViewModel community, ProfileViewModel profile, SettingsViewModel settings)
    {
        Friends = friends;
        Feed = feed;
        Home = home;
        Library = library;
        Achievements = achievements;
        Download = download;
        Market = market;
        Inventory = inventory;
        Trades = trades;
        Workshop = workshop;
        Groups = groups;
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
            "feed" => Feed,
            "library" => Library,
            "achievements" => Achievements,
            "downloads" => Download,
            "market" => Market,
            "inventory" => Inventory,
            "trades" => Trades,
            "workshop" => Workshop,
            "groups" => Groups,
            "community" => Community,
            "profile" => Profile,
            _ => Friends
        };
    }
}

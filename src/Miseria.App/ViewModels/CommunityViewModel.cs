using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miseria.App.Services;

namespace Miseria.App.ViewModels;

public partial class CommunityViewModel : ObservableObject
{
    private static readonly (uint AppId, string Name)[] FallbackApps =
    [
        (730, "Counter-Strike 2"),
        (570, "Dota 2"),
        (1091500, "Cyberpunk 2077"),
        (271590, "Grand Theft Auto V"),
        (753, "Steam")
    ];

    private readonly CommunityService _community;
    private readonly ItemIconCache _icons;
    private readonly LibraryViewModel _library;
    private bool _loaded;

    public ObservableCollection<AppChoice> NewsApps { get; } = [];

    public ObservableCollection<NewsItem> News { get; } = [];

    public ObservableCollection<StoreItem> Specials { get; } = [];

    public ObservableCollection<StoreItem> TopSellers { get; } = [];

    public ObservableCollection<StoreItem> NewReleases { get; } = [];

    [ObservableProperty]
    private AppChoice? _selectedNewsApp;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public bool HasNews => News.Count > 0;

    public CommunityViewModel(CommunityService community, ItemIconCache icons, LibraryViewModel library)
    {
        _community = community;
        _icons = icons;
        _library = library;

        NewsApps.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNewsApps));
        News.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNews));
        _library.Apps.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(RefreshAppChoices);
    }

    public bool HasNewsApps => NewsApps.Count > 0;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
            return;

        _loaded = true;
        RefreshAppChoices();
        await LoadFeaturedAsync();
        await LoadNewsAsync();
    }

    private void RefreshAppChoices()
    {
        var current = SelectedNewsApp;
        var apps = _library.Apps
            .Take(60)
            .Select(a => new AppChoice(a.AppId, a.Name))
            .ToList();

        NewsApps.Clear();

        foreach (var app in apps)
            NewsApps.Add(app);

        if (NewsApps.Count == 0)
        {
            foreach (var app in FallbackApps)
                NewsApps.Add(new AppChoice(app.AppId, app.Name));
        }

        SelectedNewsApp = current != null
            ? NewsApps.FirstOrDefault(a => a.AppId == current.AppId) ?? NewsApps.FirstOrDefault()
            : NewsApps.FirstOrDefault();
    }

    private async Task LoadNewsAsync()
    {
        var appId = SelectedNewsApp?.AppId ?? 0;

        if (appId == 0)
            return;

        try
        {
            var news = await _community.GetNewsAsync(appId);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                News.Clear();

                foreach (var snapshot in news)
                    News.Add(new NewsItem(snapshot));

                StatusText = News.Count > 0
                    ? $"Новостей: {News.Count}"
                    : "Свежих новостей нет";
            });
        }
        catch (HttpRequestException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = "Не удалось загрузить новости");
        }
    }

    private async Task LoadFeaturedAsync()
    {
        if (Specials.Count > 0)
            return;

        try
        {
            var (specials, topSellers, newReleases) = await _community.GetFeaturedAsync();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AddStoreItems(Specials, specials.Take(6));
                AddStoreItems(TopSellers, topSellers.Take(6));
                AddStoreItems(NewReleases, newReleases.Take(6));
            });
        }
        catch (HttpRequestException)
        {
        }
    }

    private void AddStoreItems(ObservableCollection<StoreItem> collection, IEnumerable<StoreItemSnapshot> items)
    {
        collection.Clear();

        foreach (var snapshot in items)
        {
            var item = new StoreItem(snapshot);
            collection.Add(item);
            _ = item.LoadImageAsync(_icons, snapshot.ImageUrl);
        }
    }

    [RelayCommand]
    private Task Refresh() => LoadNewsAsync();

    partial void OnSelectedNewsAppChanged(AppChoice? value)
    {
        if (_loaded && value != null)
            _ = LoadNewsAsync();
    }
}

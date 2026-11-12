using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miseria.App.Services;

namespace Miseria.App.ViewModels;

public partial class WorkshopViewModel : ObservableObject
{
    private static readonly (uint AppId, string Name)[] WorkshopApps =
    [
        (4000, "Garry's Mod"),
        (730, "Counter-Strike 2"),
        (440, "Team Fortress 2"),
        (563860, "Left 4 Dead 2"),
        (294100, "RimWorld"),
        (255710, "Cities: Skylines"),
        (244850, "Space Engineers"),
        (322330, "Don't Starve Together"),
        (431960, "Wallpaper Engine"),
        (227300, "Euro Truck Simulator 2"),
        (304930, "Unturned"),
        (1966720, "Lethal Company")
    ];

    private const int PageSize = 25;

    private readonly WorkshopService _workshop;
    private readonly ItemIconCache _icons;
    private readonly LibraryViewModel _library;
    private bool _loaded;
    private int _page;
    private int _total;
    private int _busy;

    public ObservableCollection<AppChoice> Apps { get; } = [];

    public ObservableCollection<WorkshopItem> Items { get; } = [];

    [ObservableProperty]
    private AppChoice? _selectedApp;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private WorkshopItem? _selectedItem;

    [ObservableProperty]
    private string _actionMessage = string.Empty;

    public bool HasItems => Items.Count > 0;

    public bool IsEmpty => !IsBusy && !HasItems;

    public bool HasMore => Items.Count < _total;

    public WorkshopViewModel(WorkshopService workshop, ItemIconCache icons, LibraryViewModel library)
    {
        _workshop = workshop;
        _icons = icons;
        _library = library;

        foreach (var app in WorkshopApps)
            Apps.Add(new AppChoice(app.AppId, app.Name));

        _selectedApp = Apps[0];

        _library.Apps.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(RefreshAppChoices);
        Items.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(HasMore));
        };

        RefreshAppChoices();
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loaded)
            return;

        _loaded = true;
        await LoadAsync();
    }

    [RelayCommand]
    private Task Search() => LoadAsync();

    [RelayCommand]
    private Task Load() => LoadAsync();

    [RelayCommand]
    private async Task LoadMoreAsync(CancellationToken cancellationToken = default)
    {
        if (!HasMore || Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return;

        IsBusy = true;

        try
        {
            var appId = SelectedApp?.AppId ?? 0;
            var result = await _workshop.QueryAsync(appId, SearchText.Trim(), _page + 1, PageSize, cancellationToken);
            _page++;

            await Dispatcher.UIThread.InvokeAsync(() => Append(result));
        }
        catch (HttpRequestException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = "Не удалось загрузить мастерскую");
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SubscribeAsync(WorkshopItem? item)
    {
        if (item == null || item.IsBusy)
            return;

        item.IsBusy = true;

        try
        {
            await _workshop.SubscribeAsync(item.AppId, item.FileId);
            ActionMessage = $"«{item.Title}» подписан. Загрузка начнётся в Steam.";
        }
        catch (HttpRequestException)
        {
            ActionMessage = "Steam отклонил подписку. Попробуйте позже.";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UnsubscribeAsync(WorkshopItem? item)
    {
        if (item == null || item.IsBusy)
            return;

        item.IsBusy = true;

        try
        {
            await _workshop.UnsubscribeAsync(item.AppId, item.FileId);
            ActionMessage = $"Подписка на «{item.Title}» отменена.";
        }
        catch (HttpRequestException)
        {
            ActionMessage = "Steam отклонил запрос. Попробуйте позже.";
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var appId = SelectedApp?.AppId ?? 0;

        if (appId == 0)
            return;

        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return;

        IsBusy = true;

        try
        {
            var result = await _workshop.QueryAsync(appId, SearchText.Trim(), 1, PageSize, cancellationToken);
            _page = 1;
            _total = result.TotalCount;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SelectedItem = null;
                ActionMessage = string.Empty;
                Items.Clear();

                foreach (var snapshot in result.Items)
                    AddItem(snapshot);

                StatusText = result.Items.Count == 0
                    ? "Ничего не найдено"
                    : $"Найдено материалов: {result.TotalCount}";
            });
        }
        catch (HttpRequestException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = "Не удалось загрузить мастерскую");
        }
        catch (InvalidOperationException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = "Сессия недоступна для загрузки мастерской");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            IsBusy = false;
        }
    }

    private void Append(WorkshopResult result)
    {
        foreach (var snapshot in result.Items)
            AddItem(snapshot);

        StatusText = $"Найдено материалов: {_total}";
    }

    private void AddItem(WorkshopFileSnapshot snapshot)
    {
        var item = new WorkshopItem(snapshot);
        Items.Add(item);
        _ = LoadIconAsync(item, snapshot.PreviewUrl);
    }

    private async Task LoadIconAsync(WorkshopItem item, string? url)
    {
        var bitmap = await _icons.LoadUrlAsync(url);

        if (bitmap == null)
            return;

        await Dispatcher.UIThread.InvokeAsync(() => item.SetIcon(bitmap));
    }

    private void RefreshAppChoices()
    {
        var current = SelectedApp;
        var apps = new List<AppChoice>(Apps);

        foreach (var app in _library.Apps.Take(30))
        {
            if (!apps.Any(a => a.AppId == app.AppId))
                apps.Add(new AppChoice(app.AppId, app.Name));
        }

        Apps.Clear();

        foreach (var app in apps)
            Apps.Add(app);

        SelectedApp = current != null
            ? Apps.FirstOrDefault(a => a.AppId == current.AppId) ?? Apps.FirstOrDefault()
            : Apps.FirstOrDefault();
    }

    partial void OnSelectedAppChanged(AppChoice? value)
    {
        if (_loaded && value != null)
            _ = LoadAsync();
    }
}

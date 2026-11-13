using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miseria.App.Services;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class AchievementsViewModel : ObservableObject
{
    private static readonly (uint AppId, string Name)[] FallbackApps =
    [
        (730, "Counter-Strike 2"),
        (570, "Dota 2"),
        (440, "Team Fortress 2"),
        (292030, "The Witcher 3: Wild Hunt")
    ];

    private readonly SteamSession _session;
    private readonly LibraryViewModel _library;
    private readonly ItemIconCache _icons;
    private readonly List<AchievementRow> _all = [];
    private bool _loaded;
    private int _busy;

    public ObservableCollection<AppChoice> Apps { get; } = [];

    public ObservableCollection<FilterChoice> Filters { get; } = [];

    public ObservableCollection<AchievementRow> Achievements { get; } = [];

    [ObservableProperty]
    private AppChoice? _selectedApp;

    [ObservableProperty]
    private FilterChoice? _selectedFilter;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _hasStats;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _emptyTitle = "Выберите игру";

    [ObservableProperty]
    private string _emptyText = "Выберите игру в списке, чтобы посмотреть её достижения.";

    public bool HasAchievements => Achievements.Count > 0;

    public bool IsEmpty => !IsBusy && Achievements.Count == 0;

    public AchievementsViewModel(SteamSession session, LibraryViewModel library, ItemIconCache icons)
    {
        _session = session;
        _library = library;
        _icons = icons;

        Filters.Add(new FilterChoice(0, "Все"));
        Filters.Add(new FilterChoice(1, "Открытые"));
        Filters.Add(new FilterChoice(2, "Заблокированные"));
        _selectedFilter = Filters[0];

        _library.Apps.CollectionChanged += OnLibraryChanged;
        _session.LoggedOut += Reset;
        Achievements.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAchievements));
            OnPropertyChanged(nameof(IsEmpty));
        };

        RefreshAppChoices();
    }

    public async Task EnsureLoadedAsync()
    {
        if (App.ScreenshotMode)
            return;

        if (_loaded)
            return;

        _loaded = true;
        await LoadAsync();
    }

    [RelayCommand]
    private Task Refresh() => LoadAsync();

    [RelayCommand]
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
            var snapshot = await _session.LoadAchievementsAsync(appId, "russian", cancellationToken);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _all.Clear();
                Achievements.Clear();
                HasStats = false;

                if (snapshot == null || snapshot.Achievements.Count == 0)
                {
                    StatusText = string.Empty;
                    EmptyTitle = snapshot == null ? "Не удалось загрузить" : "Достижений нет";
                    EmptyText = snapshot == null
                        ? "Steam не ответил на запрос достижений для этой игры."
                        : "У этой игры нет достижений или они скрыты настройками приватности.";
                    return;
                }

                foreach (var item in snapshot.Achievements)
                {
                    var row = new AchievementRow(item, _icons);
                    _all.Add(row);
                    Achievements.Add(row);
                    _ = row.LoadAsync();
                }

                var unlocked = snapshot.Achievements.Count(a => a.Unlocked);
                var total = snapshot.Achievements.Count;

                ProgressValue = total > 0 ? unlocked * 100d / total : 0d;
                HasStats = true;
                StatusText = $"Открыто {RuFormat.Number(unlocked)} из {RuFormat.Number(total)}";
                ProgressText = $"{Percent(ProgressValue)} %";
            });
        }
        catch (OperationCanceledException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _all.Clear();
                Achievements.Clear();
                HasStats = false;
                StatusText = string.Empty;
                EmptyTitle = "Не удалось загрузить";
                EmptyText = "Steam не ответил на запрос достижений для этой игры.";
            });
        }
        catch (InvalidOperationException)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _all.Clear();
                Achievements.Clear();
                HasStats = false;
                StatusText = string.Empty;
                EmptyTitle = "Сессия недоступна";
                EmptyText = "Подключитесь к Steam, чтобы загрузить достижения.";
            });
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            IsBusy = false;
        }
    }

    partial void OnSelectedAppChanged(AppChoice? value)
    {
        if (_loaded && value != null)
            _ = LoadAsync();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnSelectedFilterChanged(FilterChoice? value) => ApplyFilter();

    private void OnLibraryChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(RefreshAppChoices);

    private void ApplyFilter()
    {
        var query = SearchText.Trim();
        var filter = SelectedFilter?.Id ?? 0;

        Achievements.Clear();

        foreach (var row in _all)
        {
            if (filter == 1 && !row.IsUnlocked)
                continue;

            if (filter == 2 && row.IsUnlocked)
                continue;

            if (query.Length > 0 && !row.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;

            Achievements.Add(row);
        }

        if (_all.Count > 0 && Achievements.Count == 0)
        {
            EmptyTitle = "Ничего не найдено";
            EmptyText = "Измените запрос или фильтр, чтобы увидеть достижения.";
        }
    }

    private void RefreshAppChoices()
    {
        var current = SelectedApp;
        var apps = new List<AppChoice>();

        foreach (var fallback in FallbackApps)
            apps.Add(new AppChoice(fallback.AppId, fallback.Name));

        foreach (var app in _library.Apps.Take(40))
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

    private void Reset()
    {
        _loaded = false;
        _busy = 0;
        _all.Clear();
        Achievements.Clear();
        HasStats = false;
        StatusText = string.Empty;
        EmptyTitle = "Выберите игру";
        EmptyText = "Выберите игру в списке, чтобы посмотреть её достижения.";
    }

    private static string Percent(double value) =>
        value.ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',');
}

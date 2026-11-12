using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miseria.App.Services;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class FeedViewModel : ObservableObject
{
    private readonly ActivityService _activity;
    private readonly ItemIconCache _icons;
    private readonly SteamSession _session;
    private bool _loaded;
    private int _busy;

    public ObservableCollection<ActivityItem> Entries { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    public bool HasEntries => Entries.Count > 0;

    public bool IsEmpty => !IsBusy && !HasEntries;

    public string EmptyText => IsBusy ? string.Empty : "Лента пуста или недоступна. Активность друзей появится здесь.";

    public FeedViewModel(ActivityService activity, ItemIconCache icons, SteamSession session)
    {
        _activity = activity;
        _icons = icons;
        _session = session;

        _session.LoggedOut += () => Dispatcher.UIThread.Post(Reset);
        Entries.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasEntries));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(EmptyText));
        };
    }

    public async Task EnsureLoadedAsync()
    {
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
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return;

        IsBusy = true;

        try
        {
            var snapshots = await _activity.GetFeedAsync(cancellationToken);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Entries.Clear();

                foreach (var snapshot in snapshots)
                {
                    var item = new ActivityItem(snapshot);
                    Entries.Add(item);
                    _ = LoadAvatarAsync(item, snapshot.AvatarUrl);
                }

                StatusText = Entries.Count == 0
                    ? "Новая запись не найдена"
                    : $"Записей: {Entries.Count}";
            });
        }
        catch (HttpRequestException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = "Не удалось загрузить ленту активности");
        }
        catch (InvalidOperationException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = "Сессия недоступна для загрузки ленты");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            IsBusy = false;
        }
    }

    private async Task LoadAvatarAsync(ActivityItem item, string? url)
    {
        var bitmap = await _icons.LoadUrlAsync(url);

        if (bitmap == null)
            return;

        await Dispatcher.UIThread.InvokeAsync(() => item.SetAvatar(bitmap));
    }

    private void Reset()
    {
        _loaded = false;
        Entries.Clear();
        StatusText = string.Empty;
    }
}

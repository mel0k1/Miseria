using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Miseria.App.Services;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class GroupsViewModel : ObservableObject
{
    private readonly GroupService _groups;
    private readonly ItemIconCache _icons;
    private readonly SteamSession _session;
    private bool _loaded;
    private int _busy;

    public ObservableCollection<GroupItem> Groups { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _joinQuery = string.Empty;

    [ObservableProperty]
    private string _actionMessage = string.Empty;

    [ObservableProperty]
    private GroupItem? _selectedGroup;

    public bool HasGroups => Groups.Count > 0;

    public bool IsEmpty => !IsBusy && !HasGroups;

    public GroupsViewModel(GroupService groups, ItemIconCache icons, SteamSession session)
    {
        _groups = groups;
        _icons = icons;
        _session = session;

        _session.LoggedOut += () => Dispatcher.UIThread.Post(Reset);
        Groups.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasGroups));
            OnPropertyChanged(nameof(IsEmpty));
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
            var snapshots = await _groups.GetOwnAsync(cancellationToken);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Groups.Clear();
                SelectedGroup = null;

                foreach (var snapshot in snapshots)
                {
                    var item = new GroupItem(snapshot);
                    Groups.Add(item);
                    _ = LoadAvatarAsync(item, snapshot.AvatarUrl);
                }

                StatusText = Groups.Count == 0
                    ? "Группы не найдены или профиль скрыт настройками приватности"
                    : $"Групп: {Groups.Count}";
            });
        }
        catch (HttpRequestException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = "Не удалось загрузить список групп");
        }
        catch (InvalidOperationException)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StatusText = "Сессия недоступна для загрузки групп");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task FindAsync(CancellationToken cancellationToken = default)
    {
        var query = JoinQuery.Trim();

        if (query.Length == 0)
            return;

        ActionMessage = "Поиск группы…";

        var snapshot = await _groups.GetByLinkAsync(query, cancellationToken);

        if (snapshot == null)
        {
            ActionMessage = "Группа не найдена. Проверьте ссылку или короткое имя.";
            return;
        }

        var item = new GroupItem(snapshot);

        if (!Groups.Any(g => g.GroupId == item.GroupId))
        {
            Groups.Add(item);
            _ = LoadAvatarAsync(item, snapshot.AvatarUrl);
        }

        SelectedGroup = Groups.FirstOrDefault(g => g.GroupId == item.GroupId);
        ActionMessage = string.Empty;
        StatusText = $"Групп: {Groups.Count}";
    }

    [RelayCommand]
    private async Task JoinAsync(GroupItem? group)
    {
        var target = group ?? SelectedGroup;

        if (target == null || target.IsBusy)
            return;

        target.IsBusy = true;

        try
        {
            await _groups.JoinAsync(target.GroupId);
            ActionMessage = $"Вы вступили в группу «{target.Name}».";
        }
        catch (HttpRequestException)
        {
            ActionMessage = "Steam отклонил запрос. Возможно, вступление недоступно.";
        }
        finally
        {
            target.IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LeaveAsync(GroupItem? group)
    {
        var target = group ?? SelectedGroup;

        if (target == null || target.IsBusy)
            return;

        target.IsBusy = true;

        try
        {
            await _groups.LeaveAsync(target.GroupId);
            ActionMessage = $"Вы покинули группу «{target.Name}».";
            await LoadAsync();
        }
        catch (HttpRequestException)
        {
            ActionMessage = "Steam отклонил запрос. Попробуйте позже.";
        }
        finally
        {
            target.IsBusy = false;
        }
    }

    private async Task LoadAvatarAsync(GroupItem item, string? url)
    {
        var bitmap = await _icons.LoadUrlAsync(url);

        if (bitmap == null)
            return;

        await Dispatcher.UIThread.InvokeAsync(() => item.SetAvatar(bitmap));
    }

    private void Reset()
    {
        _loaded = false;
        Groups.Clear();
        SelectedGroup = null;
        StatusText = string.Empty;
        ActionMessage = string.Empty;
        JoinQuery = string.Empty;
    }
}

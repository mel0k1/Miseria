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
    private int _feedBusy;
    private string _announcementsGroupId = string.Empty;
    private string _discussionsGroupId = string.Empty;

    public ObservableCollection<GroupItem> Groups { get; } = [];

    public ObservableCollection<AnnouncementItem> Announcements { get; } = [];

    public ObservableCollection<DiscussionItem> Discussions { get; } = [];

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

    [ObservableProperty]
    private int _section;

    [ObservableProperty]
    private bool _isFeedBusy;

    [ObservableProperty]
    private string _feedStatusText = string.Empty;

    public bool HasGroups => Groups.Count > 0;

    public bool IsEmpty => !IsBusy && !HasGroups;

    public bool IsGroupsMode => Section == 0;

    public bool IsAnnouncementsMode => Section == 1;

    public bool IsDiscussionsMode => Section == 2;

    public string FeedHintText => SelectedGroup == null
        ? "Выберите группу слева, чтобы увидеть её анонсы"
        : IsAnnouncementsMode ? "Свежие анонсы группы" : "Последние темы форума группы";

    public bool HasAnnouncements => Announcements.Count > 0;

    public bool HasDiscussions => Discussions.Count > 0;

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
        Announcements.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasAnnouncements));
        Discussions.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDiscussions));

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(Section) or nameof(SelectedGroup))
                OnPropertyChanged(nameof(FeedHintText));
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

    [RelayCommand]
    private void ShowGroups() => SetSection(0);

    [RelayCommand]
    private Task ShowAnnouncements() => SetSectionAsync(1);

    [RelayCommand]
    private Task ShowDiscussions() => SetSectionAsync(2);

    private void SetSection(int value)
    {
        Section = value;
        OnPropertyChanged(nameof(IsGroupsMode));
        OnPropertyChanged(nameof(IsAnnouncementsMode));
        OnPropertyChanged(nameof(IsDiscussionsMode));
    }

    private async Task SetSectionAsync(int value)
    {
        SetSection(value);

        if (value == 0)
            return;

        var group = SelectedGroup;

        if (group == null)
        {
            FeedStatusText = "Выберите группу, чтобы загрузить её содержимое";
            return;
        }

        await LoadFeedAsync(group, value);
    }

    [RelayCommand]
    private Task RefreshFeed() => SelectedGroup == null
        ? Task.CompletedTask
        : LoadFeedAsync(SelectedGroup, Section, force: true);

    private async Task LoadFeedAsync(GroupItem group, int value, bool force = false)
    {
        if (Interlocked.CompareExchange(ref _feedBusy, 1, 0) != 0)
            return;

        IsFeedBusy = true;

        try
        {
            if (value == 1)
            {
                if (!force && _announcementsGroupId == group.GroupId && Announcements.Count > 0)
                    return;

                var snapshots = await _groups.GetAnnouncementsAsync(group.Snapshot);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _announcementsGroupId = group.GroupId;
                    Announcements.Clear();

                    foreach (var snapshot in snapshots)
                        Announcements.Add(new AnnouncementItem(
                            snapshot.Title,
                            snapshot.Url,
                            snapshot.Date == DateTimeOffset.MinValue ? string.Empty : RuFormat.Date(snapshot.Date),
                            snapshot.Text));

                    FeedStatusText = Announcements.Count == 0
                        ? "Анонсов нет или лента недоступна"
                        : $"Анонсов: {Announcements.Count}";
                });
            }
            else if (value == 2)
            {
                if (!force && _discussionsGroupId == group.GroupId && Discussions.Count > 0)
                    return;

                var snapshots = await _groups.GetDiscussionsAsync(group.Snapshot);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _discussionsGroupId = group.GroupId;
                    Discussions.Clear();

                    foreach (var snapshot in snapshots)
                        Discussions.Add(new DiscussionItem(
                            snapshot.Title,
                            snapshot.Url,
                            snapshot.Author,
                            BuildDiscussionMeta(snapshot)));

                    FeedStatusText = Discussions.Count == 0
                        ? "Темы не найдены или форум закрыт"
                        : $"Тем: {Discussions.Count}";
                });
            }
        }
        finally
        {
            Interlocked.Exchange(ref _feedBusy, 0);
            IsFeedBusy = false;
        }
    }

    private static string BuildDiscussionMeta(GroupDiscussionSnapshot snapshot)
    {
        var parts = new List<string>();

        if (snapshot.Author.Length > 0)
            parts.Add($"Автор: {snapshot.Author}");

        parts.Add($"Ответов: {snapshot.Replies}");

        if (snapshot.LastPost != null)
            parts.Add($"Последнее: {RuFormat.DayTime(snapshot.LastPost.Value)}");

        return string.Join(" · ", parts);
    }

    private async Task LoadAvatarAsync(GroupItem item, string? url)
    {
        var bitmap = await _icons.LoadUrlAsync(url);

        if (bitmap == null)
            return;

        await Dispatcher.UIThread.InvokeAsync(() => item.SetAvatar(bitmap));
    }

    partial void OnSelectedGroupChanged(GroupItem? value)
    {
        if (Section != 0)
            _ = value == null
                ? Task.CompletedTask
                : LoadFeedAsync(value, Section);
    }

    private void Reset()
    {
        _loaded = false;
        Groups.Clear();
        SelectedGroup = null;
        StatusText = string.Empty;
        ActionMessage = string.Empty;
        JoinQuery = string.Empty;
        Announcements.Clear();
        Discussions.Clear();
        _announcementsGroupId = string.Empty;
        _discussionsGroupId = string.Empty;
        FeedStatusText = string.Empty;
        SetSection(0);
    }
}

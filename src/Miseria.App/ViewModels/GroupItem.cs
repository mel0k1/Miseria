using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Miseria.App.Services;

namespace Miseria.App.ViewModels;

public partial class GroupItem : ObservableObject
{
    [ObservableProperty]
    private Bitmap? _avatar;

    [ObservableProperty]
    private bool _hasAvatar;

    [ObservableProperty]
    private bool _isBusy;

    public GroupSnapshot Snapshot { get; }

    public string GroupId => Snapshot.GroupId;

    public string ShortName => Snapshot.ShortName;

    public bool HasLink => ShortName.Length > 0;

    public string Name => Snapshot.Name;

    public string Headline => Snapshot.Headline;

    public bool HasHeadline => Headline.Length > 0 && Headline != Name;

    public string Summary => Snapshot.Summary;

    public bool HasSummary => Summary.Length > 0;

    public string CountText => $"Участников: {Snapshot.MemberCount} · В сети: {Snapshot.OnlineCount} · В игре: {Snapshot.InGameCount}";

    public string GroupUrl => HasLink
        ? $"https://steamcommunity.com/groups/{ShortName}"
        : $"https://steamcommunity.com/gid/{GroupId}";

    public GroupItem(GroupSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public void SetAvatar(Bitmap? bitmap)
    {
        if (bitmap == null)
            return;

        Avatar = bitmap;
        HasAvatar = true;
    }
}

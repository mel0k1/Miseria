using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Miseria.App.Services;

namespace Miseria.App.ViewModels;

public partial class ActivityItem : ObservableObject
{
    [ObservableProperty]
    private Bitmap? _avatar;

    [ObservableProperty]
    private bool _hasAvatar;

    public string ActorName { get; }

    public string ProfileUrl { get; }

    public string Text { get; }

    public string TimeText { get; }

    public bool HasProfile => ProfileUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase);

    public string Initial => ActorName.Length > 0 ? char.ToUpperInvariant(ActorName[0]).ToString() : "?";

    public ActivityItem(ActivitySnapshot snapshot)
    {
        ActorName = snapshot.ActorName;
        ProfileUrl = snapshot.ProfileUrl;
        Text = snapshot.Text;
        TimeText = snapshot.TimeText;
    }

    public void SetAvatar(Bitmap? bitmap)
    {
        if (bitmap == null)
            return;

        Avatar = bitmap;
        HasAvatar = true;
    }
}

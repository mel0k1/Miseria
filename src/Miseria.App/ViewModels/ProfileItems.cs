using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Miseria.App.Services;

namespace Miseria.App.ViewModels;

public sealed record ProfileGameRow(string Name, string HoursText);

public partial class ProfileScreenshotRow : ObservableObject
{
    private readonly ItemIconCache _icons;
    private readonly string _url;

    [ObservableProperty]
    private Bitmap? _image;

    [ObservableProperty]
    private bool _hasImage;

    public string DetailsUrl { get; }

    public ProfileScreenshotRow(string url, string detailsUrl, ItemIconCache icons)
    {
        _url = url;
        DetailsUrl = detailsUrl;
        _icons = icons;
    }

    public async Task LoadAsync()
    {
        var bitmap = await _icons.LoadUrlAsync(_url);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Image = bitmap;
            HasImage = bitmap != null;
        });
    }

    public void SetImage(Bitmap? bitmap)
    {
        if (bitmap == null)
            return;

        Image = bitmap;
        HasImage = true;
    }
}

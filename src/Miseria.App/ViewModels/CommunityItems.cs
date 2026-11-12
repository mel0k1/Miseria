using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Miseria.App.Services;

namespace Miseria.App.ViewModels;

public partial class NewsItem : ObservableObject
{
    public string Title { get; }

    public string Url { get; }

    public string FeedLabel { get; }

    public string Contents { get; }

    public string DateText { get; }

    public NewsItem(NewsSnapshot snapshot)
    {
        Title = snapshot.Title;
        Url = snapshot.Url;
        FeedLabel = snapshot.FeedLabel;
        Contents = snapshot.Contents;
        DateText = snapshot.Date == DateTimeOffset.MinValue
            ? string.Empty
            : snapshot.Date.ToLocalTime().ToString("d MMM yyyy, H:mm", Culture);
    }

    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("ru-RU");
}

public partial class StoreItem : ObservableObject
{
    [ObservableProperty]
    private Bitmap? _image;

    [ObservableProperty]
    private bool _hasImage;

    public string Name { get; }

    public string Url { get; }

    public uint AppId { get; }

    public int DiscountPercent { get; }

    public bool HasDiscount => DiscountPercent > 0;

    public string DiscountText => $"-{DiscountPercent}%";

    public string PriceText { get; }

    public StoreItem(StoreItemSnapshot snapshot)
    {
        Name = snapshot.Name;
        Url = snapshot.Url;
        AppId = snapshot.AppId;
        DiscountPercent = snapshot.DiscountPercent;
        PriceText = snapshot.FinalPriceCents > 0
            ? StorePriceFormat.Format(snapshot.FinalPriceCents, snapshot.Currency)
            : string.Empty;
    }

    public async Task LoadImageAsync(ItemIconCache icons, string? imageUrl)
    {
        var bitmap = await icons.LoadUrlAsync(imageUrl);

        if (bitmap == null)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Image = bitmap;
            HasImage = true;
        });
    }
}

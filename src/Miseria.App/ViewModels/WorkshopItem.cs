using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Miseria.App.Services;

namespace Miseria.App.ViewModels;

public partial class WorkshopItem : ObservableObject
{
    [ObservableProperty]
    private Bitmap? _icon;

    [ObservableProperty]
    private bool _hasIcon;

    [ObservableProperty]
    private bool _isBusy;

    public WorkshopFileSnapshot Snapshot { get; }

    public string FileId => Snapshot.FileId;

    public string Title => Snapshot.Title;

    public string Description => Snapshot.Description;

    public bool HasDescription => Description.Length > 0;

    public uint AppId => Snapshot.AppId;

    public bool HasCreator => Snapshot.CreatorName.Length > 0;

    public string CreatorText => HasCreator ? Snapshot.CreatorName : $"Автор {Snapshot.CreatorId}";

    public string StatsText => $"Подписок: {FormatCount(Snapshot.Subscriptions)} · В избранном: {FormatCount(Snapshot.Favorited)} · Просмотров: {FormatCount(Snapshot.Views)}";

    public string SizeText => FormatSize(Snapshot.FileSize);

    public bool HasSize => Snapshot.FileSize > 0;

    public bool HasUpdated => Snapshot.Updated != DateTimeOffset.MinValue;

    public string UpdatedText => Snapshot.Updated == DateTimeOffset.MinValue
        ? string.Empty
        : $"Обновлено {RuFormat.Date(Snapshot.Updated.ToLocalTime())}";

    public string TagsText => Snapshot.Tags;

    public bool HasTags => TagsText.Length > 0;

    public string DetailsUrl => $"https://steamcommunity.com/sharedfiles/filedetails/?id={FileId}";

    public WorkshopItem(WorkshopFileSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public void SetIcon(Bitmap? bitmap)
    {
        if (bitmap == null)
            return;

        Icon = bitmap;
        HasIcon = true;
    }

    private static string FormatCount(long value)
    {
        if (value >= 1_000_000)
            return $"{(value / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture)} млн";

        if (value >= 1_000)
            return $"{(value / 1_000d).ToString("0.#", CultureInfo.InvariantCulture)} тыс.";

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
            return string.Empty;

        if (bytes < 1024)
            return $"{bytes} Б";

        if (bytes < 1024 * 1024)
            return $"{(bytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture)} КБ";

        return $"{(bytes / 1024d / 1024d).ToString("0.#", CultureInfo.InvariantCulture)} МБ";
    }
}

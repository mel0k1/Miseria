using System.Globalization;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Miseria.App.Services;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public sealed record FilterChoice(int Id, string Name);

public partial class AchievementRow : ObservableObject
{
    private readonly ItemIconCache _icons;
    private readonly AchievementSnapshot _snapshot;

    [ObservableProperty]
    private Bitmap? _image;

    [ObservableProperty]
    private bool _hasImage;

    public string Name { get; }

    public string? Description { get; }

    public bool HasDescription => Description != null;

    public bool IsUnlocked { get; }

    public bool IsSecret { get; }

    public string GlobalText { get; }

    public string UnlockText { get; }

    public bool HasUnlockText { get; }

    public string ProgressText { get; }

    public bool HasProgress { get; }

    public AchievementRow(AchievementSnapshot snapshot, ItemIconCache icons)
    {
        _snapshot = snapshot;
        _icons = icons;

        Name = snapshot.Name.Length > 0 ? snapshot.Name : "Без названия";
        IsUnlocked = snapshot.Unlocked;
        IsSecret = snapshot.Hidden && !snapshot.Unlocked;
        Description = IsSecret ? null : EmptyAsNull(snapshot.Description);
        GlobalText = $"Открыто у {Percent(snapshot.GlobalPercent)}% игроков";
        HasUnlockText = snapshot.UnlockTime.HasValue;
        UnlockText = snapshot.UnlockTime.HasValue ? $"Открыто {RuFormat.Date(snapshot.UnlockTime.Value)}" : string.Empty;
        HasProgress = !snapshot.Unlocked && snapshot.MaxProgress > snapshot.MinProgress && snapshot.Progress > 0;
        ProgressText = HasProgress ? $"Прогресс: {RuFormat.Number(snapshot.Progress)} из {RuFormat.Number(snapshot.MaxProgress)}" : string.Empty;
    }

    public async Task LoadAsync()
    {
        var bitmap = await _icons.LoadUrlAsync(IsUnlocked ? _snapshot.IconUrl : _snapshot.IconGrayUrl);

        if (bitmap == null)
            return;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Image = bitmap;
            HasImage = true;
        });
    }

    public void SetImage(Bitmap? bitmap)
    {
        if (bitmap == null)
            return;

        Image = bitmap;
        HasImage = true;
    }

    private static string Percent(double value) =>
        value.ToString("0.#", CultureInfo.InvariantCulture).Replace('.', ',');

    private static string? EmptyAsNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

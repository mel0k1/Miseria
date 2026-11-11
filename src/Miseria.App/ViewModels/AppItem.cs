using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Miseria.Core;

namespace Miseria.App.ViewModels;

public partial class AppItem : ObservableObject
{
    public uint AppId { get; }

    public string Name { get; }

    public string Type { get; }

    public string? IconHash { get; }

    [ObservableProperty]
    private Bitmap? _icon;

    [ObservableProperty]
    private bool _hasIcon;

    public AppItem(AppSnapshot snapshot)
    {
        AppId = snapshot.AppId;
        Name = snapshot.Name;
        Type = snapshot.Type;
        IconHash = snapshot.IconHash;
    }

    public string TypeText => Type switch
    {
        "game" => "Игра",
        "dlc" => "Дополнение",
        "application" => "Приложение",
        "tool" => "Инструмент",
        "demo" => "Демо",
        "music" => "Музыка",
        "video" => "Видео",
        "hardware" => "Устройство",
        "guide" => "Руководство",
        "series" => "Серия",
        "episode" => "Эпизод",
        "mod" => "Мод",
        "beta" => "Бета-версия",
        "" => "Приложение",
        _ => char.ToUpperInvariant(Type[0]) + Type[1..]
    };

    public string Initial => Name.Length > 0 ? char.ToUpperInvariant(Name[0]).ToString() : "?";
}

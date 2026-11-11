using Miseria.Core;

namespace Miseria.App.ViewModels;

public class AppItem(AppSnapshot snapshot)
{
    public uint AppId => snapshot.AppId;

    public string Name => snapshot.Name;

    public string Type => snapshot.Type;

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

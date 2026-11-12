using System.Globalization;

namespace Miseria.App.Services;

public static class RuFormat
{
    private static readonly string[] Months =
    [
        "января", "февраля", "марта", "апреля", "мая", "июня",
        "июля", "августа", "сентября", "октября", "ноября", "декабря"
    ];

    public static string Month(int month) => Months[Math.Clamp(month, 1, 12) - 1];

    public static string Date(DateTimeOffset value) =>
        $"{value.Day} {Month(value.Month)} {value.Year}, {value.Hour:00}:{value.Minute:00}";

    public static string DayTime(DateTimeOffset value) =>
        $"{value.Day} {Month(value.Month)}, {value.Hour:00}:{value.Minute:00}";
}

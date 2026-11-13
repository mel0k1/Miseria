using System.Globalization;
using System.Text;

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

    public static string Hours(double hours)
    {
        if (hours < 0)
            hours = 0;

        if (hours >= 100)
            return $"{Number((long)Math.Round(hours))} ч.";

        var text = ((Math.Round(hours * 10d) / 10d)
            .ToString("0.#", CultureInfo.InvariantCulture))
            .Replace('.', ',');

        return $"{text} ч.";
    }

    public static string Number(long value)
    {
        var digits = value.ToString(CultureInfo.InvariantCulture);
        var builder = new StringBuilder();

        for (var i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0)
                builder.Append(' ');

            builder.Append(digits[i]);
        }

        return builder.ToString();
    }
}

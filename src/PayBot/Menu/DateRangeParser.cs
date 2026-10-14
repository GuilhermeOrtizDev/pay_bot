using System.Globalization;
using System.Text.RegularExpressions;

namespace PayBot.Menu;

public static partial class DateRangeParser
{
    public static bool TryParse(string? text, out DateOnly from, out DateOnly to)
    {
        from = default;
        to = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = Splitter().Split(text.Trim());
        if (parts.Length != 2)
            return false;

        if (!DateOnly.TryParseExact(parts[0], "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out from))
            return false;
        if (!DateOnly.TryParseExact(parts[1], "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out to))
            return false;

        return from <= to;
    }

    public static bool TryParseMonths(string? text, out DateOnly from, out DateOnly to)
    {
        from = default;
        to = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = Splitter().Split(text.Trim());
        if (parts.Length is not (1 or 2) || !TryMonth(parts[0], out var start))
            return false;

        var end = start;
        if (parts.Length == 2 && !TryMonth(parts[1], out end))
            return false;
        if (start > end)
            return false;

        from = start;
        to = end.AddMonths(1).AddDays(-1);
        return true;
    }

    private static bool TryMonth(string text, out DateOnly month)
    {
        month = default;
        var formats = new[] { "MM/yyyy", "M/yyyy" };
        if (!DateOnly.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;

        month = new DateOnly(parsed.Year, parsed.Month, 1);
        return true;
    }

    [GeneratedRegex(@"\s+[-–—]\s+|\s*[-–—]\s*")]
    private static partial Regex Splitter();
}

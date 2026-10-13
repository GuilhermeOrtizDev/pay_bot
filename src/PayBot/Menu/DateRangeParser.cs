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

    [GeneratedRegex(@"\s+[-–—]\s+|\s*[-–—]\s*")]
    private static partial Regex Splitter();
}

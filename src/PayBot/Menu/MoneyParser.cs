using System.Globalization;

namespace PayBot.Menu;

public static class MoneyParser
{
    public static bool TryParse(string? text, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var value = text.Trim();
        if (value.StartsWith("R$", StringComparison.OrdinalIgnoreCase))
            value = value[2..].Trim();

        value = value.Replace(" ", "").Replace("\u00A0", "");
        if (value.Length == 0)
            return false;

        if (value.Contains(',') && value.Contains('.'))
            value = value.Replace(".", "").Replace(',', '.');
        else if (value.Contains(','))
            value = value.Replace(',', '.');
        else if (value.Contains('.'))
        {
            var lastDot = value.LastIndexOf('.');
            var decimals = value.Length - lastDot - 1;
            var singleDot = value.IndexOf('.') == lastDot;
            if (!(singleDot && decimals is 1 or 2))
                value = value.Replace(".", "");
        }

        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
            return false;
        if (amount <= 0 || amount > 999_999_999.99m)
            return false;

        amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        return true;
    }
}

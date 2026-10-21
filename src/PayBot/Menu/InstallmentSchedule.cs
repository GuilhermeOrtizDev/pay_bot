using System.Globalization;
using PayBot.Domain;

namespace PayBot.Menu;

public readonly record struct InstallmentMonth(DateOnly Date, decimal Amount);

public static class InstallmentSchedule
{
    public static bool TryAmounts(decimal amount, int count, InstallmentAmountKind kind, out IReadOnlyList<decimal> amounts)
    {
        amounts = [];
        if (count is < 1 or > 480 || amount <= 0)
            return false;

        var cents = ToCents(amount);
        if (cents <= 0)
            return false;

        var built = new decimal[count];
        if (kind == InstallmentAmountKind.Installment)
        {
            var parcel = cents / 100m;
            for (var index = 0; index < count; index++)
                built[index] = parcel;
        }
        else
        {
            var each = cents / count;
            if (each <= 0)
                return false;

            var remainder = cents % count;
            for (var index = 0; index < count; index++)
            {
                var monthCents = index == count - 1 ? each + remainder : each;
                built[index] = monthCents / 100m;
            }
        }

        amounts = built;
        return true;
    }

    public static bool TryBuild(
        decimal amount,
        int count,
        InstallmentAmountKind kind,
        DateOnly start,
        out IReadOnlyList<InstallmentMonth> months)
    {
        months = [];
        if (!TryAmounts(amount, count, kind, out var amounts))
            return false;

        var monthStart = new DateOnly(start.Year, start.Month, 1);
        var built = new InstallmentMonth[amounts.Count];
        for (var index = 0; index < amounts.Count; index++)
            built[index] = new InstallmentMonth(monthStart.AddMonths(index), amounts[index]);

        months = built;
        return true;
    }

    public static string EntryMessageId(string? messageId, int number)
    {
        var id = messageId ?? "";
        if (number <= 1)
            return id.Length <= 128 ? id : id[..128];

        var suffix = ":" + number.ToString(CultureInfo.InvariantCulture);
        var room = Math.Max(0, 128 - suffix.Length);
        var baseId = id.Length <= room ? id : id[..room];
        return baseId + suffix;
    }

    private static long ToCents(decimal amount) =>
        decimal.ToInt64(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
}

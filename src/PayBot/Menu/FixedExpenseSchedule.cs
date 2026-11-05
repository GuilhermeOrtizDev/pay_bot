using PayBot.Domain;

namespace PayBot.Menu;

public static class FixedExpenseSchedule
{
    public static IEnumerable<DateOnly> MonthsInRange(
        DateOnly startMonth,
        DateOnly? endMonth,
        DateOnly rangeFrom,
        DateOnly rangeTo)
    {
        var start = FirstOfMonth(startMonth);
        var end = endMonth is DateOnly closed ? FirstOfMonth(closed) : FirstOfMonth(rangeTo);
        var from = FirstOfMonth(rangeFrom);
        var to = FirstOfMonth(rangeTo);

        var cursor = start > from ? start : from;
        var last = end < to ? end : to;
        while (cursor <= last)
        {
            yield return cursor;
            cursor = cursor.AddMonths(1);
        }
    }

    public static bool CoversMonth(FixedExpense expense, DateOnly month)
    {
        var m = FirstOfMonth(month);
        if (FirstOfMonth(expense.StartMonth) > m)
            return false;
        return expense.EndMonth is not DateOnly end || FirstOfMonth(end) >= m;
    }

    public static bool OverlapsRange(FixedExpense expense, DateOnly from, DateOnly to) =>
        MonthsInRange(expense.StartMonth, expense.EndMonth, from, to).Any();

    public static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);
}

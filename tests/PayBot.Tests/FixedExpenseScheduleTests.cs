using PayBot.Menu;

namespace PayBot.Tests;

public class FixedExpenseScheduleTests
{
    [Fact]
    public void Months_in_range_are_inclusive_of_end()
    {
        var months = FixedExpenseSchedule.MonthsInRange(
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 11, 30)).ToList();

        Assert.Equal(
            [new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1)],
            months);
    }

    [Fact]
    public void Open_ended_uses_range_end()
    {
        var months = FixedExpenseSchedule.MonthsInRange(
            new DateOnly(2026, 9, 1),
            null,
            new DateOnly(2026, 9, 15),
            new DateOnly(2026, 11, 5)).ToList();

        Assert.Equal(
            [new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 1)],
            months);
    }
}

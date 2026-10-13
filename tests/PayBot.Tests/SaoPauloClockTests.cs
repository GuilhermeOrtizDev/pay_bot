using PayBot.Time;

namespace PayBot.Tests;

public class SaoPauloClockTests
{
    [Fact]
    public void Noon_on_a_charge_month_is_stored_in_utc()
    {
        var instant = SaoPauloClock.AtNoon(new DateOnly(2026, 10, 1));

        Assert.Equal(TimeSpan.Zero, instant.Offset);
        Assert.Equal(new DateOnly(2026, 10, 1), SaoPauloClock.LocalDate(instant));
    }
}

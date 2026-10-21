using PayBot.Domain;
using PayBot.Menu;

namespace PayBot.Tests;

public class InstallmentScheduleTests
{
    [Fact]
    public void Total_puts_the_remainder_on_the_last_month()
    {
        Assert.True(InstallmentSchedule.TryBuild(1000m, 3, InstallmentAmountKind.Total, new DateOnly(2026, 10, 1), out var months));

        Assert.Equal(
            [
                new InstallmentMonth(new DateOnly(2026, 10, 1), 333.33m),
                new InstallmentMonth(new DateOnly(2026, 11, 1), 333.33m),
                new InstallmentMonth(new DateOnly(2026, 12, 1), 333.34m)
            ],
            months);
        Assert.Equal(1000m, months.Sum(month => month.Amount));
    }

    [Fact]
    public void Installment_amount_repeats_from_the_start_month()
    {
        Assert.True(InstallmentSchedule.TryBuild(120m, 10, InstallmentAmountKind.Installment, new DateOnly(2026, 11, 15), out var months));

        Assert.Equal(10, months.Count);
        Assert.Equal(new DateOnly(2026, 11, 1), months[0].Date);
        Assert.Equal(new DateOnly(2027, 8, 1), months[^1].Date);
        Assert.All(months, month => Assert.Equal(120m, month.Amount));
    }

    [Fact]
    public void Total_smaller_than_one_cent_per_month_is_rejected()
    {
        Assert.False(InstallmentSchedule.TryAmounts(0.02m, 10, InstallmentAmountKind.Total, out _));
        Assert.True(InstallmentSchedule.TryAmounts(0.10m, 10, InstallmentAmountKind.Total, out var amounts));
        Assert.All(amounts, amount => Assert.Equal(0.01m, amount));
    }

    [Fact]
    public void Later_rows_get_a_unique_message_id_within_128_characters()
    {
        Assert.Equal("abc", InstallmentSchedule.EntryMessageId("abc", 1));
        Assert.Equal("abc:2", InstallmentSchedule.EntryMessageId("abc", 2));

        var id = new string('m', 128);
        Assert.Equal(id, InstallmentSchedule.EntryMessageId(id, 1));
        Assert.Equal(128, InstallmentSchedule.EntryMessageId(id, 2).Length);
        Assert.EndsWith(":2", InstallmentSchedule.EntryMessageId(id, 2));
        Assert.Equal(128, InstallmentSchedule.EntryMessageId(id, 480).Length);
        Assert.EndsWith(":480", InstallmentSchedule.EntryMessageId(id, 480));
    }
}

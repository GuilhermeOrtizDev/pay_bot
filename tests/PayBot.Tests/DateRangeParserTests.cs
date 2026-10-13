using PayBot.Menu;

namespace PayBot.Tests;

public class DateRangeParserTests
{
    [Fact]
    public void Parses_brazilian_range()
    {
        Assert.True(DateRangeParser.TryParse("10/05/2026 - 20/05/2026", out var from, out var to));
        Assert.Equal(new DateOnly(2026, 5, 10), from);
        Assert.Equal(new DateOnly(2026, 5, 20), to);
    }

    [Fact]
    public void Parses_range_without_spaces()
    {
        Assert.True(DateRangeParser.TryParse("10/05/2026-20/05/2026", out var from, out var to));
        Assert.Equal(new DateOnly(2026, 5, 10), from);
        Assert.Equal(new DateOnly(2026, 5, 20), to);
    }

    [Theory]
    [InlineData("20/05/2026 - 10/05/2026")]
    [InlineData("10-05-2026 - 20-05-2026")]
    [InlineData("maio")]
    public void Rejects_invalid_ranges(string text)
    {
        Assert.False(DateRangeParser.TryParse(text, out _, out _));
    }
}

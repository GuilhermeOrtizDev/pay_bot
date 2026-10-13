using PayBot.Domain;
using PayBot.Menu;

namespace PayBot.Tests;

public class MoneyParserTests
{
    [Theory]
    [InlineData("32,50", 32.50)]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("32.50", 32.50)]
    [InlineData("R$ 10", 10)]
    [InlineData("0,50", 0.50)]
    [InlineData("1.234", 1234)]
    public void Accepts_brazilian_amounts(string text, decimal expected)
    {
        Assert.True(MoneyParser.TryParse(text, out var amount));
        Assert.Equal(expected, amount);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0,00")]
    [InlineData("-5")]
    [InlineData("")]
    [InlineData("abc")]
    public void Rejects_invalid_amounts(string text)
    {
        Assert.False(MoneyParser.TryParse(text, out _));
    }
}

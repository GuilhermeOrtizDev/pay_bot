using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PayBot.Data;
using PayBot.Domain;
using PayBot.Menu;
using PayBot.Summaries;

namespace PayBot.Tests;

public class SummaryTests
{
    [Fact]
    public async Task Month_summary_ignores_other_months()
    {
        await using var db = await TestDb.CreateAsync();
        var card = Card(CardKind.Credit, "Nubank");
        db.Cards.Add(card);
        db.Transactions.Add(Income(new DateOnly(2026, 10, 2), 3000, "salario"));
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 3), 50, PaymentMethod.Pix));
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 5), 400, PaymentMethod.CreditCard, card, 10));
        db.Transactions.Add(Expense(new DateOnly(2026, 9, 1), 999, PaymentMethod.Pix));
        await db.SaveChangesAsync();

        var text = await FormatAsync(db, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "Resumo de 01/10/2026 a 31/10/2026");

        Assert.Equal(
            """
            Resumo de 01/10/2026 a 31/10/2026
            Entradas: R$ 3.000,00
            Saidas: R$ 450,00
            Saldo: R$ 2.550,00

            Saidas
            - Pix: R$ 50,00
            - Cartao credito: R$ 400,00
              - Nubank: R$ 400,00 em 10x

            Entradas
            - 02/10/2026 salario: R$ 3.000,00
            """.Replace("\r\n", "\n"),
            text.Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task Year_summary_uses_the_calendar_year()
    {
        await using var db = await TestDb.CreateAsync();
        db.Transactions.Add(Expense(new DateOnly(2026, 1, 1), 10, PaymentMethod.Pix));
        db.Transactions.Add(Expense(new DateOnly(2026, 12, 31), 15, PaymentMethod.Other));
        db.Transactions.Add(Expense(new DateOnly(2025, 12, 31), 999, PaymentMethod.Pix));
        await db.SaveChangesAsync();

        var report = await new SummaryService(db).BuildAsync(
            new SummaryQuery(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "Resumo de 01/01/2026 a 31/12/2026"),
            CancellationToken.None);

        Assert.Equal(0, report.IncomeTotal);
        Assert.Equal(25, report.ExpenseTotal);
        Assert.Equal(-25, report.IncomeTotal - report.ExpenseTotal);
    }

    [Fact]
    public async Task Total_summary_includes_every_entry()
    {
        await using var db = await TestDb.CreateAsync();
        db.Transactions.Add(Income(new DateOnly(2024, 1, 1), 100, "antiga"));
        db.Transactions.Add(Expense(new DateOnly(2026, 8, 1), 40, PaymentMethod.Fixed));
        await db.SaveChangesAsync();

        var report = await new SummaryService(db).BuildAsync(
            new SummaryQuery(null, null, "Resumo total"),
            CancellationToken.None);

        Assert.Equal(100, report.IncomeTotal);
        Assert.Equal(40, report.ExpenseTotal);
        Assert.Equal("Resumo total", report.Title);
    }

    [Fact]
    public async Task Date_filter_is_inclusive()
    {
        await using var db = await TestDb.CreateAsync();
        db.Transactions.Add(Expense(new DateOnly(2026, 5, 9), 10, PaymentMethod.Pix));
        db.Transactions.Add(Expense(new DateOnly(2026, 5, 10), 20, PaymentMethod.Pix));
        db.Transactions.Add(Income(new DateOnly(2026, 5, 20), 30, "freela"));
        db.Transactions.Add(Expense(new DateOnly(2026, 5, 21), 40, PaymentMethod.Pix));
        await db.SaveChangesAsync();

        var report = await new SummaryService(db).BuildAsync(
            new SummaryQuery(new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 20), "Resumo de 10/05/2026 a 20/05/2026"),
            CancellationToken.None);

        Assert.Equal(30, report.IncomeTotal);
        Assert.Equal(20, report.ExpenseTotal);
        Assert.Equal(10, report.IncomeTotal - report.ExpenseTotal);
    }

    private static async Task<string> FormatAsync(PayBotDbContext db, DateOnly from, DateOnly to, string title)
    {
        var report = await new SummaryService(db).BuildAsync(new SummaryQuery(from, to, title), CancellationToken.None);
        return SummaryFormatter.Format(report);
    }

    private static Card Card(CardKind kind, string name) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        NormalizedName = name.ToLowerInvariant(),
        Kind = kind,
        CreatedAt = DateTimeOffset.UnixEpoch,
        LastUsedAt = DateTimeOffset.UnixEpoch
    };

    private static LedgerEntry Income(DateOnly date, decimal amount, string description) => new()
    {
        Id = Guid.NewGuid(),
        OccurredAt = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        LocalDate = date,
        Direction = Direction.Income,
        Amount = amount,
        Description = description,
        WhatsAppMessageId = Guid.NewGuid().ToString("N")
    };

    private static LedgerEntry Expense(DateOnly date, decimal amount, PaymentMethod method, Card? card = null, int? installments = null) => new()
    {
        Id = Guid.NewGuid(),
        OccurredAt = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        LocalDate = date,
        Direction = Direction.Expense,
        Amount = amount,
        PaymentMethod = method,
        Installments = installments,
        CardId = card?.Id,
        Card = card,
        WhatsAppMessageId = Guid.NewGuid().ToString("N")
    };

    [Fact]
    public void Pdf_contains_the_summary_title()
    {
        var report = SummaryAggregator.Build(
            "Resumo total",
            [new SummaryRow(new DateOnly(2026, 10, 2), Direction.Income, 10, null, "salario", null, null)]);
        var pdf = SummaryPdf.Render(report);
        var text = System.Text.Encoding.ASCII.GetString(pdf);

        Assert.StartsWith("%PDF", text);
        Assert.Contains("Resumo total", text);
        Assert.Contains("salario", text);
    }
}

file static class TestDb
{
    public static async Task<PayBotDbContext> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PayBotDbContext>().UseSqlite(connection).Options;
        var db = new PayBotDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return db;
    }
}

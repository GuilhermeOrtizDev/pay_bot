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
            TestDb.Tenant,
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
            TestDb.Tenant,
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
            TestDb.Tenant,
            new SummaryQuery(new DateOnly(2026, 5, 10), new DateOnly(2026, 5, 20), "Resumo de 10/05/2026 a 20/05/2026"),
            CancellationToken.None);

        Assert.Equal(30, report.IncomeTotal);
        Assert.Equal(20, report.ExpenseTotal);
        Assert.Equal(10, report.IncomeTotal - report.ExpenseTotal);
    }

    [Fact]
    public async Task Method_filter_shows_only_expenses_of_that_method()
    {
        await using var db = await TestDb.CreateAsync();
        var card = Card(CardKind.Credit, "Nubank");
        db.Cards.Add(card);
        db.Transactions.Add(Income(new DateOnly(2026, 10, 2), 3000, "salario"));
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 3), 50, PaymentMethod.Pix, description: "padaria"));
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 5), 400, PaymentMethod.CreditCard, card, 10));
        await db.SaveChangesAsync();

        var report = await new SummaryService(db).BuildAsync(
            TestDb.Tenant,
            new SummaryQuery(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "Resumo de 01/10/2026 a 31/10/2026 - Pix", PaymentMethod.Pix),
            CancellationToken.None);

        Assert.True(report.ExpensesOnly);
        Assert.Equal(0, report.IncomeTotal);
        Assert.Empty(report.Incomes);
        Assert.Equal(50, report.ExpenseTotal);
        Assert.Equal(
            """
            Resumo de 01/10/2026 a 31/10/2026 - Pix
            Saidas: R$ 50,00

            Saidas
            - Pix: R$ 50,00
            """.Replace("\r\n", "\n"),
            SummaryFormatter.Format(report).Replace("\r\n", "\n"));

        var pdf = System.Text.Encoding.ASCII.GetString(SummaryPdf.Render(report));
        Assert.DoesNotContain("Entradas", pdf);
        Assert.DoesNotContain("Saldo", pdf);
    }

    [Fact]
    public async Task Card_filter_keeps_only_that_card()
    {
        await using var db = await TestDb.CreateAsync();
        var nubank = Card(CardKind.Credit, "Nubank");
        var inter = Card(CardKind.Credit, "Inter");
        db.Cards.AddRange(nubank, inter);
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 5), 400, PaymentMethod.CreditCard, nubank));
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 6), 70, PaymentMethod.CreditCard, inter));
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 7), 20, PaymentMethod.Pix));
        await db.SaveChangesAsync();

        var service = new SummaryService(db);
        var one = await service.BuildAsync(
            TestDb.Tenant,
            new SummaryQuery(null, null, "Resumo total - Cartao credito Nubank", PaymentMethod.CreditCard, nubank.Id),
            CancellationToken.None);
        var every = await service.BuildAsync(
            TestDb.Tenant,
            new SummaryQuery(null, null, "Resumo total - Cartao credito", PaymentMethod.CreditCard),
            CancellationToken.None);

        Assert.Equal(400, one.ExpenseTotal);
        Assert.Equal("Nubank", Assert.Single(Assert.Single(one.Methods).Cards).Name);
        Assert.Equal(470, every.ExpenseTotal);
        Assert.Equal(2, Assert.Single(every.Methods).Cards.Count);
    }

    private static async Task<string> FormatAsync(PayBotDbContext db, DateOnly from, DateOnly to, string title)
    {
        var report = await new SummaryService(db).BuildAsync(TestDb.Tenant, new SummaryQuery(from, to, title), CancellationToken.None);
        return SummaryFormatter.Format(report);
    }

    private static Card Card(CardKind kind, string name) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TestDb.Tenant,
        Name = name,
        NormalizedName = name.ToLowerInvariant(),
        Kind = kind,
        CreatedAt = DateTimeOffset.UnixEpoch,
        LastUsedAt = DateTimeOffset.UnixEpoch
    };

    private static LedgerEntry Income(DateOnly date, decimal amount, string description, Guid? tenant = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenant ?? TestDb.Tenant,
        OccurredAt = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        LocalDate = date,
        Direction = Direction.Income,
        Amount = amount,
        Description = description,
        WhatsAppMessageId = Guid.NewGuid().ToString("N")
    };

    private static LedgerEntry Expense(
        DateOnly date,
        decimal amount,
        PaymentMethod method,
        Card? card = null,
        int? installments = null,
        Guid? tenant = null,
        DateTimeOffset? createdAt = null,
        string? description = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenant ?? TestDb.Tenant,
        CreatedAt = createdAt ?? date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        OccurredAt = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        LocalDate = date,
        Direction = Direction.Expense,
        Amount = amount,
        PaymentMethod = method,
        Description = description,
        Installments = installments,
        CardId = card?.Id,
        Card = card,
        WhatsAppMessageId = Guid.NewGuid().ToString("N")
    };

    [Fact]
    public async Task Month_balance_is_income_minus_expenses()
    {
        await using var db = await TestDb.CreateAsync();
        db.Transactions.Add(Income(new DateOnly(2026, 10, 2), 3000, "salario"));
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 3), 450, PaymentMethod.Pix));
        db.Transactions.Add(Expense(new DateOnly(2026, 9, 1), 999, PaymentMethod.Pix));
        await db.SaveChangesAsync();

        var balance = await new SummaryService(db).MonthBalanceAsync(TestDb.Tenant, new DateOnly(2026, 10, 15), CancellationToken.None);

        Assert.Equal(2550m, balance);
    }

    [Fact]
    public async Task Summary_and_balance_only_see_their_tenant()
    {
        await using var db = await TestDb.CreateAsync();
        db.Transactions.Add(Income(new DateOnly(2026, 10, 2), 3000, "salario"));
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 3), 100, PaymentMethod.Pix));
        db.Transactions.Add(Income(new DateOnly(2026, 10, 2), 777, "outro", TestDb.OtherTenant));
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 3), 555, PaymentMethod.Pix, tenant: TestDb.OtherTenant));
        await db.SaveChangesAsync();

        var service = new SummaryService(db);
        var mine = await service.BuildAsync(TestDb.Tenant, new SummaryQuery(null, null, "Resumo total"), CancellationToken.None);
        var other = await service.BuildAsync(TestDb.OtherTenant, new SummaryQuery(null, null, "Resumo total"), CancellationToken.None);

        Assert.Equal(3000, mine.IncomeTotal);
        Assert.Equal(100, mine.ExpenseTotal);
        Assert.Equal(777, other.IncomeTotal);
        Assert.Equal(555, other.ExpenseTotal);
        Assert.Equal(2900m, await service.MonthBalanceAsync(TestDb.Tenant, new DateOnly(2026, 10, 1), CancellationToken.None));
        Assert.Equal(222m, await service.MonthBalanceAsync(TestDb.OtherTenant, new DateOnly(2026, 10, 1), CancellationToken.None));
    }

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

    [Fact]
    public void Pdf_lists_each_expense()
    {
        var report = SummaryAggregator.Build(
            "Resumo de 01/10/2026 a 31/10/2026",
            [
                new SummaryRow(new DateOnly(2026, 10, 3), Direction.Expense, 50, PaymentMethod.Pix, "padaria", null, null),
                new SummaryRow(new DateOnly(2026, 10, 5), Direction.Expense, 1200, PaymentMethod.CreditCard, "mercado", "Nubank", 10),
                new SummaryRow(new DateOnly(2026, 10, 1), Direction.Expense, 80, PaymentMethod.CreditCard, "netflix", "Inter", null, true),
                new SummaryRow(new DateOnly(2026, 10, 2), Direction.Income, 3000, null, "salario", null, null)
            ]);
        var text = System.Text.Encoding.ASCII.GetString(SummaryPdf.Render(report));

        Assert.Contains("01/10/2026 Cartao credito Inter fixo: R$ 80,00 - netflix", text);
        Assert.Contains("03/10/2026 Pix: R$ 50,00 - padaria", text);
        Assert.Contains("05/10/2026 Cartao credito Nubank em 10x: R$ 1.200,00 - mercado", text);
        Assert.Contains("02/10/2026 salario: R$ 3.000,00", text);
    }

    [Fact]
    public async Task Summary_orders_expenses_by_created_at()
    {
        await using var db = await TestDb.CreateAsync();
        var nubank = Card(CardKind.Credit, "Nubank");
        var inter = Card(CardKind.Credit, "Inter");
        db.Cards.AddRange(nubank, inter);
        var first = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        var second = first.AddDays(1);
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 20), 50, PaymentMethod.Pix, createdAt: second, description: "padaria"));
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 1), 80, PaymentMethod.CreditCard, nubank, createdAt: first, description: "mercado"));
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 2), 30, PaymentMethod.CreditCard, inter, createdAt: second.AddHours(1), description: "netflix"));
        await db.SaveChangesAsync();

        var report = await new SummaryService(db).BuildAsync(
            TestDb.Tenant,
            new SummaryQuery(null, null, "Resumo total"),
            CancellationToken.None);
        var text = SummaryFormatter.Format(report);
        var pdf = System.Text.Encoding.ASCII.GetString(SummaryPdf.Render(report));

        Assert.Equal(["mercado", "padaria", "netflix"], report.Expenses.Select(line => line.Description));
        Assert.True(text.IndexOf("Nubank", StringComparison.Ordinal) < text.IndexOf("Inter", StringComparison.Ordinal));
        Assert.True(pdf.IndexOf("mercado", StringComparison.Ordinal) < pdf.IndexOf("padaria", StringComparison.Ordinal));
        Assert.True(pdf.IndexOf("padaria", StringComparison.Ordinal) < pdf.IndexOf("netflix", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Latest_delete_candidates_follow_created_at()
    {
        await using var db = await TestDb.CreateAsync();
        var older = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        var newer = older.AddDays(2);
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 20), 10, PaymentMethod.Pix, createdAt: older, description: "antigo"));
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 1), 20, PaymentMethod.Pix, createdAt: newer, description: "recente"));
        db.Transactions.Add(Expense(new DateOnly(2026, 9, 1), 30, PaymentMethod.Pix, createdAt: newer.AddMinutes(1), description: "ultimo"));
        await db.SaveChangesAsync();

        var entries = await db.Transactions.ToListAsync();
        var latest = PayBot.Webhooks.InboundProcessor
            .OrderDeleteCandidates(entries, new DeleteListRequest(null, null, 10))
            .Select(entry => entry.Description);
        var ranged = PayBot.Webhooks.InboundProcessor
            .OrderDeleteCandidates(entries, new DeleteListRequest(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 30))
            .Select(entry => entry.Description);

        Assert.Equal(["ultimo", "recente", "antigo"], latest);
        Assert.Equal(["antigo", "recente"], ranged);
    }
}

file static class TestDb
{
    public static readonly Guid Tenant = PayBot.Domain.Tenant.DefaultId;
    public static readonly Guid OtherTenant = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static async Task<PayBotDbContext> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PayBotDbContext>().UseSqlite(connection).Options;
        var db = new PayBotDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.Tenants.Add(new PayBot.Domain.Tenant { Id = Tenant, Name = "Principal", NormalizedName = "principal" });
        db.Tenants.Add(new PayBot.Domain.Tenant { Id = OtherTenant, Name = "Outro", NormalizedName = "outro" });
        await db.SaveChangesAsync();
        return db;
    }
}

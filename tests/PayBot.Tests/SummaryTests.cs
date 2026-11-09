using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PayBot.Data;
using PayBot.Domain;
using PayBot.Menu;
using PayBot.Summaries;
using PayBot.Time;

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
            Saldo anterior: -R$ 999,00
            Saldo: R$ 1.551,00

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
        Assert.True(report.ShowOpeningBalance);
        Assert.Equal(-999, report.OpeningBalance);
        Assert.Equal(-1024, report.Balance);
    }

    [Fact]
    public async Task Total_summary_includes_every_entry()
    {
        await using var db = await TestDb.CreateAsync();
        db.Transactions.Add(Income(new DateOnly(2024, 1, 1), 100, "antiga"));
        db.FixedExpenses.Add(Fixed(new DateOnly(2026, 8, 1), 40, "aluguel", new DateOnly(2026, 8, 1)));
        await db.SaveChangesAsync();

        var report = await new SummaryService(db).BuildAsync(
            TestDb.Tenant,
            new SummaryQuery(null, null, "Resumo total"),
            CancellationToken.None);

        Assert.Equal(100, report.IncomeTotal);
        Assert.Equal(40, report.ExpenseTotal);
        Assert.Equal("Resumo total", report.Title);
        Assert.False(report.ShowOpeningBalance);
        Assert.Equal(0, report.OpeningBalance);
        Assert.Equal(60, report.Balance);
        Assert.DoesNotContain("Saldo anterior", SummaryFormatter.Format(report));
    }

    [Fact]
    public async Task Fixed_expense_expands_across_months_until_end()
    {
        await using var db = await TestDb.CreateAsync();
        db.FixedExpenses.Add(Fixed(new DateOnly(2026, 9, 1), 100, "aluguel"));
        await db.SaveChangesAsync();

        var service = new SummaryService(db);
        var open = await service.BuildAsync(
            TestDb.Tenant,
            new SummaryQuery(new DateOnly(2026, 9, 1), new DateOnly(2026, 11, 30), "Resumo"),
            CancellationToken.None);
        Assert.Equal(300, open.ExpenseTotal);
        Assert.Equal(3, open.Expenses.Count);

        db.FixedExpenses.First().EndMonth = new DateOnly(2026, 10, 1);
        await db.SaveChangesAsync();

        var closed = await service.BuildAsync(
            TestDb.Tenant,
            new SummaryQuery(new DateOnly(2026, 9, 1), new DateOnly(2026, 11, 30), "Resumo"),
            CancellationToken.None);
        Assert.Equal(200, closed.ExpenseTotal);
        Assert.Equal(2, closed.Expenses.Count);
        Assert.DoesNotContain(closed.Expenses, line => line.Date.Month == 11);
    }

    [Fact]
    public async Task Month_balance_includes_open_fixed_expense()
    {
        await using var db = await TestDb.CreateAsync();
        db.Transactions.Add(Income(new DateOnly(2026, 10, 2), 1000, "salario"));
        db.FixedExpenses.Add(Fixed(new DateOnly(2026, 9, 1), 150, "net"));
        await db.SaveChangesAsync();

        var service = new SummaryService(db);
        Assert.Equal(700m, await service.MonthBalanceAsync(TestDb.Tenant, new DateOnly(2026, 10, 1), CancellationToken.None));

        var november = await service.BuildAsync(
            TestDb.Tenant,
            new SummaryQuery(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), "Resumo"),
            CancellationToken.None);
        Assert.Equal(700, november.OpeningBalance);
        Assert.Equal(550, november.Balance);
    }

    [Fact]
    public async Task Fixed_method_filter_reads_only_fixed_expenses()
    {
        await using var db = await TestDb.CreateAsync();
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 1), 20, PaymentMethod.Pix));
        db.FixedExpenses.Add(Fixed(new DateOnly(2026, 10, 1), 80, "condo"));
        await db.SaveChangesAsync();

        var report = await new SummaryService(db).BuildAsync(
            TestDb.Tenant,
            new SummaryQuery(
                new DateOnly(2026, 10, 1),
                new DateOnly(2026, 10, 31),
                "Resumo - Despesa Fixa",
                PaymentMethod.Fixed),
            CancellationToken.None);

        Assert.Equal(80, report.ExpenseTotal);
        Assert.Single(report.Expenses);
        Assert.Equal(PaymentMethod.Fixed, report.Expenses[0].Method);
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
        Assert.True(report.ShowOpeningBalance);
        Assert.Equal(-10, report.OpeningBalance);
        Assert.Equal(0, report.Balance);
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

    [Fact]
    public async Task Compact_summary_shows_only_method_and_card_totals()
    {
        await using var db = await TestDb.CreateAsync();
        var black = Card(CardKind.Credit, "Black");
        var platinum = Card(CardKind.Credit, "Platinum");
        db.Cards.AddRange(black, platinum);
        db.Transactions.Add(Income(new DateOnly(2026, 10, 2), 3000, "salario"));
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 3), 50, PaymentMethod.Pix, description: "padaria"));
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 4), 200, PaymentMethod.CreditCard, black));
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 5), 100, PaymentMethod.CreditCard, black));
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 6), 70, PaymentMethod.CreditCard, platinum));
        await db.SaveChangesAsync();

        var report = await new SummaryService(db).BuildAsync(
            TestDb.Tenant,
            new SummaryQuery(
                new DateOnly(2026, 10, 1),
                new DateOnly(2026, 10, 31),
                "Resumo de 01/10/2026 a 31/10/2026",
                Compact: true),
            CancellationToken.None);

        Assert.True(report.Compact);
        Assert.Equal(
            """
            Resumo de 01/10/2026 a 31/10/2026
            Entradas: R$ 3.000,00
            Saidas: R$ 420,00
            Saldo anterior: R$ 0,00
            Saldo: R$ 2.580,00

            Saidas
            - Pix: R$ 50,00
            - Cartao credito Black: R$ 300,00
            - Cartao credito Platinum: R$ 70,00
            """.Replace("\r\n", "\n"),
            SummaryFormatter.Format(report).Replace("\r\n", "\n"));

        var pdf = System.Text.Encoding.ASCII.GetString(SummaryPdf.Render(report));
        Assert.Contains("Cartao credito Black: R$ 300,00", pdf);
        Assert.Contains("Cartao credito Platinum: R$ 70,00", pdf);
        Assert.Contains("Pix: R$ 50,00", pdf);
        Assert.DoesNotContain("padaria", pdf);
        Assert.DoesNotContain("salario", pdf);
        Assert.DoesNotContain("03/10/2026", pdf);
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
        NormalizedName = BulkEntry.Fold(name),
        Kind = kind,
        CreatedAt = DateTimeOffset.UnixEpoch,
        LastUsedAt = DateTimeOffset.UnixEpoch
    };

    private static LedgerEntry Income(
        DateOnly date,
        decimal amount,
        string description,
        Guid? tenant = null,
        DateTimeOffset? createdAt = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenant ?? TestDb.Tenant,
        CreatedAt = createdAt ?? SaoPauloClock.AtNoon(date),
        OccurredAt = SaoPauloClock.AtNoon(date),
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
        CreatedAt = createdAt ?? SaoPauloClock.AtNoon(date),
        OccurredAt = SaoPauloClock.AtNoon(date),
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

    private static FixedExpense Fixed(
        DateOnly startMonth,
        decimal amount,
        string description,
        DateOnly? endMonth = null,
        Guid? tenant = null,
        PaymentMethod method = PaymentMethod.Fixed,
        Card? card = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenant ?? TestDb.Tenant,
        CreatedAt = SaoPauloClock.AtNoon(startMonth),
        Amount = amount,
        Description = description,
        Method = method,
        CardId = card?.Id,
        Card = card,
        StartMonth = startMonth,
        EndMonth = endMonth,
        WhatsAppMessageId = Guid.NewGuid().ToString("N")
    };

    [Fact]
    public async Task Month_balance_includes_previous_month_closing()
    {
        await using var db = await TestDb.CreateAsync();
        db.Transactions.Add(Income(new DateOnly(2026, 10, 2), 3000, "salario"));
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 3), 450, PaymentMethod.Pix));
        db.Transactions.Add(Expense(new DateOnly(2026, 9, 1), 999, PaymentMethod.Pix));
        await db.SaveChangesAsync();

        var balance = await new SummaryService(db).MonthBalanceAsync(TestDb.Tenant, new DateOnly(2026, 10, 15), CancellationToken.None);

        Assert.Equal(1551m, balance);
    }

    [Fact]
    public async Task Empty_month_carries_previous_closing_balance_forward()
    {
        await using var db = await TestDb.CreateAsync();
        db.Transactions.Add(Income(new DateOnly(2026, 9, 1), 300, "salario"));
        await db.SaveChangesAsync();

        var service = new SummaryService(db);

        Assert.Equal(300m, await service.MonthBalanceAsync(TestDb.Tenant, new DateOnly(2026, 10, 1), CancellationToken.None));
        Assert.Equal(300m, await service.MonthBalanceAsync(TestDb.Tenant, new DateOnly(2026, 11, 1), CancellationToken.None));
    }

    [Fact]
    public async Task Next_month_uses_previous_closing_not_sum_of_monthly_saldos()
    {
        await using var db = await TestDb.CreateAsync();
        db.Transactions.Add(Income(new DateOnly(2026, 9, 1), 300, "set"));
        db.Transactions.Add(Income(new DateOnly(2026, 10, 1), 100, "out"));
        await db.SaveChangesAsync();

        var service = new SummaryService(db);
        var october = await service.BuildAsync(
            TestDb.Tenant,
            new SummaryQuery(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "Resumo de 01/10/2026 a 31/10/2026"),
            CancellationToken.None);
        var november = await service.MonthBalanceAsync(TestDb.Tenant, new DateOnly(2026, 11, 1), CancellationToken.None);

        Assert.Equal(300, october.OpeningBalance);
        Assert.Equal(400, october.Balance);
        Assert.Equal(400m, november);
        Assert.NotEqual(700m, november);
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
                new SummaryRow(new DateOnly(2026, 10, 3), Direction.Expense, 50, PaymentMethod.Pix, "padaria", null, null, CreatedAt: SaoPauloClock.AtNoon(new DateOnly(2026, 10, 3))),
                new SummaryRow(new DateOnly(2026, 10, 5), Direction.Expense, 1200, PaymentMethod.CreditCard, "mercado", "Nubank", 10, CreatedAt: SaoPauloClock.AtNoon(new DateOnly(2026, 10, 5))),
                new SummaryRow(new DateOnly(2026, 10, 1), Direction.Expense, 80, PaymentMethod.CreditCard, "netflix", "Inter", null, true, SaoPauloClock.AtNoon(new DateOnly(2026, 10, 1))),
                new SummaryRow(new DateOnly(2026, 10, 2), Direction.Income, 3000, null, "salario", null, null, CreatedAt: SaoPauloClock.AtNoon(new DateOnly(2026, 10, 2)))
            ]);
        var text = System.Text.Encoding.ASCII.GetString(SummaryPdf.Render(report));

        Assert.Contains("01/10/2026 Cartao credito Inter fixo: R$ 80,00 - netflix", text);
        Assert.Contains("03/10/2026 Pix: R$ 50,00 - padaria", text);
        Assert.Contains("05/10/2026 Cartao credito Nubank em 10x: R$ 1.200,00 - mercado", text);
        Assert.Contains("02/10/2026 salario: R$ 3.000,00", text);
    }

    [Fact]
    public async Task Printed_dates_use_created_at_not_local_date()
    {
        await using var db = await TestDb.CreateAsync();
        var charged = new DateOnly(2026, 11, 1);
        var created = SaoPauloClock.AtNoon(new DateOnly(2026, 10, 5));
        db.Transactions.Add(Expense(charged, 50, PaymentMethod.Pix, createdAt: created, description: "padaria"));
        db.Transactions.Add(Income(charged, 100, "salario", createdAt: created));
        await db.SaveChangesAsync();

        var report = await new SummaryService(db).BuildAsync(
            TestDb.Tenant,
            new SummaryQuery(charged, charged.AddMonths(1).AddDays(-1), "Resumo de 01/11/2026 a 30/11/2026"),
            CancellationToken.None);
        var text = SummaryFormatter.Format(report);
        var pdf = System.Text.Encoding.ASCII.GetString(SummaryPdf.Render(report));

        Assert.Equal(new DateOnly(2026, 10, 5), Assert.Single(report.Expenses).Date);
        Assert.Equal(new DateOnly(2026, 10, 5), Assert.Single(report.Incomes).Date);
        Assert.Contains("- 05/10/2026 salario", text);
        Assert.Contains("05/10/2026 Pix: R$ 50,00 - padaria", pdf);
        Assert.DoesNotContain("- 01/11/2026", text);
        Assert.DoesNotContain("01/11/2026 Pix", pdf);
        Assert.DoesNotContain("01/11/2026 salario", pdf);
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

    [Fact]
    public async Task Delete_candidates_can_filter_by_direction()
    {
        await using var db = await TestDb.CreateAsync();
        var older = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 20), 10, PaymentMethod.Pix, createdAt: older, description: "gasto"));
        var income = Income(new DateOnly(2026, 10, 15), 100, "salario");
        income.CreatedAt = older.AddMinutes(1);
        db.Transactions.Add(income);
        await db.SaveChangesAsync();

        var entries = await db.Transactions.ToListAsync();
        var incomes = PayBot.Webhooks.InboundProcessor
            .OrderDeleteCandidates(entries, new DeleteListRequest(null, null, 10, Direction.Income))
            .Select(entry => entry.Description);
        var expenses = PayBot.Webhooks.InboundProcessor
            .OrderDeleteCandidates(entries, new DeleteListRequest(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 30, Direction.Expense))
            .Select(entry => entry.Description);

        Assert.Equal(["salario"], incomes);
        Assert.Equal(["gasto"], expenses);
    }

    [Fact]
    public void Delete_list_shows_reference_month_and_generated_day()
    {
        var created = SaoPauloClock.AtNoon(new DateOnly(2026, 10, 5));
        var entry = Expense(new DateOnly(2026, 11, 1), 50, PaymentMethod.Pix, createdAt: created, description: "padaria");

        Assert.Equal("padaria - R$ 50,00 - 11/2026 gerado 05/10/2026", DeleteListFormatter.Line(entry));
        Assert.Equal(
            """
            Selecione o id para remover
            0 - Voltar Menu Inicial
            1 - padaria - R$ 50,00 - 11/2026 gerado 05/10/2026
            """.Replace("\r\n", "\n"),
            DeleteListFormatter.Format([entry]).Replace("\r\n", "\n"));
    }

    [Fact]
    public void Fixed_delete_list_shows_start_and_optional_end()
    {
        var open = Fixed(new DateOnly(2026, 9, 1), 1500, "aluguel");
        Assert.Equal("aluguel - R$ 1.500,00 - desde 09/2026", DeleteListFormatter.FixedLine(open));

        var closed = Fixed(new DateOnly(2026, 9, 1), 1500, "aluguel", new DateOnly(2026, 11, 1));
        Assert.Equal("aluguel - R$ 1.500,00 - desde 09/2026 ate 11/2026", DeleteListFormatter.FixedLine(closed));

        var card = Card(CardKind.Credit, "Inter");
        var cardFixed = Fixed(
            new DateOnly(2026, 9, 1),
            80,
            "netflix",
            method: PaymentMethod.CreditCard,
            card: card);
        Assert.Equal(
            "netflix - R$ 80,00 - Cartao credito Inter - desde 09/2026",
            DeleteListFormatter.FixedLine(cardFixed));
    }

    [Fact]
    public async Task Card_delete_without_records_removes_the_card()
    {
        await using var db = await TestDb.CreateAsync();
        var card = Card(CardKind.Credit, "Nubank");
        db.Cards.Add(card);
        await db.SaveChangesAsync();

        var result = await PayBot.Webhooks.InboundProcessor.ApplyCardDeleteAsync(
            db,
            TestDb.Tenant,
            new PendingCardDelete(card.Id, false),
            CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(MenuStep.RemoveMenu, result.NextStep);
        Assert.StartsWith("Cartao removido", result.Reply);
        Assert.Empty(db.Cards.Where(item => item.Id == card.Id));
    }

    [Fact]
    public async Task Card_delete_with_records_asks_before_cascade()
    {
        await using var db = await TestDb.CreateAsync();
        var card = Card(CardKind.Credit, "Nubank");
        db.Cards.Add(card);
        db.Transactions.Add(Expense(new DateOnly(2026, 10, 5), 100, PaymentMethod.CreditCard, card, description: "mercado"));
        db.FixedExpenses.Add(Fixed(
            new DateOnly(2026, 9, 1),
            80,
            "netflix",
            method: PaymentMethod.CreditCard,
            card: card));
        await db.SaveChangesAsync();

        var ask = await PayBot.Webhooks.InboundProcessor.ApplyCardDeleteAsync(
            db,
            TestDb.Tenant,
            new PendingCardDelete(card.Id, false),
            CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(MenuStep.AwaitingCardDeleteConfirm, ask.NextStep);
        Assert.Equal(card.Id, ask.Draft.CardId);
        Assert.Contains("Este cartao tem registros vinculados", ask.Reply);
        Assert.Single(db.Cards.Where(item => item.Id == card.Id));
        Assert.Equal(1, db.Transactions.Count(item => item.CardId == card.Id));
        Assert.Equal(1, db.FixedExpenses.Count(item => item.CardId == card.Id));

        var removed = await PayBot.Webhooks.InboundProcessor.ApplyCardDeleteAsync(
            db,
            TestDb.Tenant,
            new PendingCardDelete(card.Id, true),
            CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(MenuStep.RemoveMenu, removed.NextStep);
        Assert.StartsWith("Cartao removido", removed.Reply);
        Assert.Empty(db.Cards.Where(item => item.Id == card.Id));
        Assert.Empty(db.Transactions.Where(item => item.CardId == card.Id));
        Assert.Empty(db.FixedExpenses.Where(item => item.CardId == card.Id));
    }

    [Fact]
    public async Task Card_fixed_expense_expands_and_respects_filters()
    {
        await using var db = await TestDb.CreateAsync();
        var inter = Card(CardKind.Credit, "Inter");
        var nubank = Card(CardKind.Credit, "Nubank");
        db.Cards.AddRange(inter, nubank);
        db.FixedExpenses.Add(Fixed(
            new DateOnly(2026, 9, 1),
            80,
            "netflix",
            method: PaymentMethod.CreditCard,
            card: inter));
        db.FixedExpenses.Add(Fixed(new DateOnly(2026, 9, 1), 1500, "aluguel"));
        await db.SaveChangesAsync();

        var service = new SummaryService(db);
        var period = await service.BuildAsync(
            TestDb.Tenant,
            new SummaryQuery(new DateOnly(2026, 9, 1), new DateOnly(2026, 11, 30), "Resumo"),
            CancellationToken.None);
        Assert.Equal(80 * 3 + 1500 * 3, period.ExpenseTotal);

        var byCard = await service.BuildAsync(
            TestDb.Tenant,
            new SummaryQuery(
                new DateOnly(2026, 10, 1),
                new DateOnly(2026, 10, 31),
                "Resumo - Cartao credito Inter",
                PaymentMethod.CreditCard,
                inter.Id),
            CancellationToken.None);
        Assert.Equal(80, byCard.ExpenseTotal);
        Assert.Single(byCard.Expenses);
        Assert.Equal("netflix", byCard.Expenses[0].Description);
        Assert.True(byCard.Expenses[0].IsFixed);

        var onlyFixed = await service.BuildAsync(
            TestDb.Tenant,
            new SummaryQuery(
                new DateOnly(2026, 10, 1),
                new DateOnly(2026, 10, 31),
                "Resumo - Despesa Fixa",
                PaymentMethod.Fixed),
            CancellationToken.None);
        Assert.Equal(1500, onlyFixed.ExpenseTotal);
        Assert.DoesNotContain(onlyFixed.Expenses, line => line.Description == "netflix");
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

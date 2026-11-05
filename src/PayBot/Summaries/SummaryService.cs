using Microsoft.EntityFrameworkCore;
using PayBot.Data;
using PayBot.Domain;
using PayBot.Menu;

namespace PayBot.Summaries;

public sealed class SummaryService(PayBotDbContext db)
{
    public async Task<SummaryReport> BuildAsync(Guid tenantId, SummaryQuery query, CancellationToken cancellationToken)
    {
        var expensesOnly = query.Method is not null;
        var source = db.Transactions.AsNoTracking().Where(entry => entry.TenantId == tenantId);
        if (query.From is DateOnly from && query.To is DateOnly to)
            source = source.Where(entry => entry.LocalDate >= from && entry.LocalDate <= to);
        if (query.Method is PaymentMethod method)
            source = source.Where(entry => entry.Direction == Direction.Expense && entry.PaymentMethod == method);
        if (query.CardId is Guid cardId)
            source = source.Where(entry => entry.CardId == cardId);

        var rows = await source
            .Select(entry => new SummaryRow(
                entry.LocalDate,
                entry.Direction,
                entry.Amount,
                entry.PaymentMethod,
                entry.Description,
                entry.Card != null ? entry.Card.Name : null,
                entry.Installments,
                entry.IsFixed,
                entry.CreatedAt))
            .ToListAsync(cancellationToken);

        var report = SummaryAggregator.Build(query.Title, rows, expensesOnly);
        if (expensesOnly || query.From is not DateOnly openingFrom)
            return report;

        var openingBalance = await BalanceBeforeAsync(tenantId, openingFrom, cancellationToken);
        return report with { OpeningBalance = openingBalance, ShowOpeningBalance = true };
    }

    public async Task<decimal> MonthBalanceAsync(Guid tenantId, DateOnly date, CancellationToken cancellationToken)
    {
        var start = new DateOnly(date.Year, date.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var openingBalance = await BalanceBeforeAsync(tenantId, start, cancellationToken);
        var rows = await db.Transactions.AsNoTracking()
            .Where(entry => entry.TenantId == tenantId && entry.LocalDate >= start && entry.LocalDate <= end)
            .Select(entry => new { entry.Direction, entry.Amount })
            .ToListAsync(cancellationToken);

        var income = rows.Where(row => row.Direction == Direction.Income).Sum(row => row.Amount);
        var expense = rows.Where(row => row.Direction == Direction.Expense).Sum(row => row.Amount);
        return openingBalance + income - expense;
    }

    private async Task<decimal> BalanceBeforeAsync(Guid tenantId, DateOnly before, CancellationToken cancellationToken)
    {
        var rows = await db.Transactions.AsNoTracking()
            .Where(entry => entry.TenantId == tenantId && entry.LocalDate < before)
            .Select(entry => new { entry.Direction, entry.Amount })
            .ToListAsync(cancellationToken);

        var income = rows.Where(row => row.Direction == Direction.Income).Sum(row => row.Amount);
        var expense = rows.Where(row => row.Direction == Direction.Expense).Sum(row => row.Amount);
        return income - expense;
    }
}

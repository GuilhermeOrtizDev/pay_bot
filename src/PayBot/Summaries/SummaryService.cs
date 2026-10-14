using Microsoft.EntityFrameworkCore;
using PayBot.Data;
using PayBot.Domain;
using PayBot.Menu;

namespace PayBot.Summaries;

public sealed class SummaryService(PayBotDbContext db)
{
    public async Task<SummaryReport> BuildAsync(SummaryQuery query, CancellationToken cancellationToken)
    {
        var source = db.Transactions.AsNoTracking();
        if (query.From is DateOnly from && query.To is DateOnly to)
            source = source.Where(entry => entry.LocalDate >= from && entry.LocalDate <= to);

        var rows = await source
            .Select(entry => new SummaryRow(
                entry.LocalDate,
                entry.Direction,
                entry.Amount,
                entry.PaymentMethod,
                entry.Description,
                entry.Card != null ? entry.Card.Name : null,
                entry.Installments,
                entry.IsFixed))
            .ToListAsync(cancellationToken);

        return SummaryAggregator.Build(query.Title, rows);
    }

    public async Task<decimal> MonthBalanceAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var start = new DateOnly(date.Year, date.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var rows = await db.Transactions.AsNoTracking()
            .Where(entry => entry.LocalDate >= start && entry.LocalDate <= end)
            .Select(entry => new { entry.Direction, entry.Amount })
            .ToListAsync(cancellationToken);

        var income = rows.Where(row => row.Direction == Direction.Income).Sum(row => row.Amount);
        var expense = rows.Where(row => row.Direction == Direction.Expense).Sum(row => row.Amount);
        return income - expense;
    }
}

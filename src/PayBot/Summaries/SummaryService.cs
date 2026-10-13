using Microsoft.EntityFrameworkCore;
using PayBot.Data;
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
                entry.Installments))
            .ToListAsync(cancellationToken);

        return SummaryAggregator.Build(query.Title, rows);
    }
}

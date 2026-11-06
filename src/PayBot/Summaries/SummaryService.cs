using Microsoft.EntityFrameworkCore;
using PayBot.Data;
using PayBot.Domain;
using PayBot.Menu;
using PayBot.Time;

namespace PayBot.Summaries;

public sealed class SummaryService(PayBotDbContext db)
{
    public async Task<SummaryReport> BuildAsync(Guid tenantId, SummaryQuery query, CancellationToken cancellationToken)
    {
        var expensesOnly = query.Method is not null;
        var methodFilter = query.Method;
        var rows = new List<SummaryRow>();

        if (methodFilter is null or not PaymentMethod.Fixed)
        {
            var source = db.Transactions.AsNoTracking()
                .Where(entry => entry.TenantId == tenantId
                    && entry.PaymentMethod != PaymentMethod.Fixed
                    && !entry.IsFixed);
            if (query.From is DateOnly from && query.To is DateOnly to)
                source = source.Where(entry => entry.LocalDate >= from && entry.LocalDate <= to);
            if (methodFilter is PaymentMethod method)
                source = source.Where(entry => entry.Direction == Direction.Expense && entry.PaymentMethod == method);
            if (query.CardId is Guid cardId)
                source = source.Where(entry => entry.CardId == cardId);

            rows.AddRange(await source
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
                .ToListAsync(cancellationToken));
        }

        rows.AddRange(await ExpandFixedAsync(
            tenantId,
            query.From,
            query.To,
            methodFilter,
            query.CardId,
            cancellationToken));

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
            .Where(entry => entry.TenantId == tenantId
                && entry.PaymentMethod != PaymentMethod.Fixed
                && !entry.IsFixed
                && entry.LocalDate >= start
                && entry.LocalDate <= end)
            .Select(entry => new { entry.Direction, entry.Amount })
            .ToListAsync(cancellationToken);

        var income = rows.Where(row => row.Direction == Direction.Income).Sum(row => row.Amount);
        var expense = rows.Where(row => row.Direction == Direction.Expense).Sum(row => row.Amount);
        var fixedTotal = await FixedTotalAsync(tenantId, start, end, cancellationToken);
        return openingBalance + income - expense - fixedTotal;
    }

    private async Task<decimal> BalanceBeforeAsync(Guid tenantId, DateOnly before, CancellationToken cancellationToken)
    {
        var rows = await db.Transactions.AsNoTracking()
            .Where(entry => entry.TenantId == tenantId
                && entry.PaymentMethod != PaymentMethod.Fixed
                && !entry.IsFixed
                && entry.LocalDate < before)
            .Select(entry => new { entry.Direction, entry.Amount })
            .ToListAsync(cancellationToken);

        var income = rows.Where(row => row.Direction == Direction.Income).Sum(row => row.Amount);
        var expense = rows.Where(row => row.Direction == Direction.Expense).Sum(row => row.Amount);
        var beforeMonth = FixedExpenseSchedule.FirstOfMonth(before);
        var fixedTotal = beforeMonth > DateOnly.MinValue
            ? await FixedTotalAsync(tenantId, DateOnly.MinValue, beforeMonth.AddDays(-1), cancellationToken)
            : 0m;
        return income - expense - fixedTotal;
    }

    private async Task<decimal> FixedTotalAsync(
        Guid tenantId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken)
    {
        var expanded = await ExpandFixedAsync(tenantId, from, to, null, null, cancellationToken);
        return expanded.Sum(row => row.Amount);
    }

    private async Task<List<SummaryRow>> ExpandFixedAsync(
        Guid tenantId,
        DateOnly? from,
        DateOnly? to,
        PaymentMethod? methodFilter,
        Guid? cardId,
        CancellationToken cancellationToken)
    {
        var today = SaoPauloClock.Today();
        var currentMonth = FixedExpenseSchedule.FirstOfMonth(today);
        var query = db.FixedExpenses.AsNoTracking()
            .Include(entry => entry.Card)
            .Where(entry => entry.TenantId == tenantId);

        if (methodFilter is PaymentMethod method)
            query = query.Where(entry => entry.Method == method);
        if (cardId is Guid id)
            query = query.Where(entry => entry.CardId == id);

        var expenses = await query.ToListAsync(cancellationToken);

        DateOnly rangeFrom;
        DateOnly rangeTo;
        if (from is DateOnly f && to is DateOnly t)
        {
            rangeFrom = f;
            rangeTo = t;
        }
        else
        {
            if (expenses.Count == 0)
                return [];

            rangeFrom = expenses.Min(entry => entry.StartMonth);
            rangeTo = expenses
                .Select(entry => entry.EndMonth ?? currentMonth)
                .DefaultIfEmpty(currentMonth)
                .Max();
            if (rangeTo < rangeFrom)
                rangeTo = rangeFrom;
        }

        var rows = new List<SummaryRow>();
        foreach (var expense in expenses)
        {
            var isCardFixed = expense.Method is PaymentMethod.CreditCard or PaymentMethod.DebitCard;
            foreach (var month in FixedExpenseSchedule.MonthsInRange(
                         expense.StartMonth,
                         expense.EndMonth,
                         rangeFrom,
                         rangeTo))
            {
                rows.Add(new SummaryRow(
                    month,
                    Direction.Expense,
                    expense.Amount,
                    expense.Method,
                    expense.Description,
                    expense.Card?.Name,
                    null,
                    isCardFixed,
                    expense.CreatedAt));
            }
        }

        return rows;
    }
}

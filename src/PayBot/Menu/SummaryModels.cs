using PayBot.Domain;

namespace PayBot.Menu;

public sealed record SummaryRow(
    DateOnly LocalDate,
    Direction Direction,
    decimal Amount,
    PaymentMethod? PaymentMethod,
    string? Description,
    string? CardName,
    int? Installments,
    bool IsFixed = false);

public sealed record CardSpend(string Name, decimal Amount, int? Installments);

public sealed record MethodTotal(PaymentMethod Method, decimal Total, IReadOnlyList<CardSpend> Cards);

public sealed record IncomeLine(DateOnly Date, string Description, decimal Amount);

public sealed record ExpenseLine(
    DateOnly Date,
    PaymentMethod Method,
    string? CardName,
    int? Installments,
    bool IsFixed,
    string Description,
    decimal Amount);

public sealed record SummaryReport(
    string Title,
    decimal IncomeTotal,
    decimal ExpenseTotal,
    IReadOnlyList<MethodTotal> Methods,
    IReadOnlyList<IncomeLine> Incomes,
    IReadOnlyList<ExpenseLine> Expenses);

public static class SummaryAggregator
{
    public static SummaryReport Build(string title, IReadOnlyList<SummaryRow> rows)
    {
        var incomes = rows
            .Where(row => row.Direction == Direction.Income)
            .OrderBy(row => row.LocalDate)
            .ThenBy(row => row.Description)
            .ToList();
        var expenses = rows
            .Where(row => row.Direction == Direction.Expense)
            .OrderBy(row => row.LocalDate)
            .ThenBy(row => row.Description)
            .ToList();

        var methods = expenses
            .GroupBy(row => row.PaymentMethod ?? PaymentMethod.Other)
            .Select(group => new MethodTotal(
                group.Key,
                group.Sum(row => row.Amount),
                group.Key is PaymentMethod.CreditCard or PaymentMethod.DebitCard
                    ? group
                        .OrderBy(row => row.LocalDate)
                        .ThenBy(row => row.CardName)
                        .Select(row => new CardSpend(row.CardName ?? "Cartao", row.Amount, row.Installments))
                        .ToList()
                    : []))
            .OrderBy(method => method.Method)
            .ToList();

        return new SummaryReport(
            title,
            incomes.Sum(row => row.Amount),
            expenses.Sum(row => row.Amount),
            methods,
            incomes.Select(row => new IncomeLine(row.LocalDate, row.Description ?? "", row.Amount)).ToList(),
            expenses.Select(row => new ExpenseLine(
                row.LocalDate,
                row.PaymentMethod ?? PaymentMethod.Other,
                row.CardName,
                row.Installments,
                row.IsFixed,
                row.Description ?? "",
                row.Amount)).ToList());
    }
}

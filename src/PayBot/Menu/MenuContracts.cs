using PayBot.Domain;

namespace PayBot.Menu;

public sealed record KnownCard(Guid Id, string Name, CardKind Kind);

public sealed record PendingExpense(
    PaymentMethod Method,
    decimal Amount,
    int? Installments,
    Guid? CardId,
    string? CardName,
    bool CreateCard);

public sealed record PendingIncome(decimal Amount, string Description);

public sealed record SummaryQuery(DateOnly? From, DateOnly? To, string Title);

public sealed record MenuTurn(
    string Reply,
    MenuStep NextStep,
    SessionDraft Draft,
    PendingExpense? Expense,
    PendingIncome? Income,
    SummaryQuery? Summary);

using PayBot.Domain;

namespace PayBot.Menu;

public sealed record KnownCard(Guid Id, string Name, CardKind Kind);

public sealed record PendingExpense(
    PaymentMethod Method,
    decimal Amount,
    int? Installments,
    Guid? CardId,
    string? CardName,
    bool CreateCard,
    string Description,
    bool IsFixed,
    DateOnly? ChargeMonth);

public sealed record DeleteListRequest(DateOnly? From, DateOnly? To, int Take);

public sealed record PendingIncome(decimal Amount, string Description);

public sealed record SummaryQuery(DateOnly? From, DateOnly? To, string Title);

public sealed record MenuTurn(
    string Reply,
    MenuStep NextStep,
    SessionDraft Draft,
    PendingExpense? Expense,
    PendingIncome? Income,
    SummaryQuery? Summary,
    DeleteListRequest? DeleteList = null,
    Guid? DeleteId = null,
    bool AsPdf = false);

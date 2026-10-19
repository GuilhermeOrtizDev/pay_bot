using PayBot.Domain;
using PayBot.Menu;

namespace PayBot.Ai;

public enum AiAction
{
    Other = 0,
    Expense = 1,
    Income = 2,
    Summary = 3,
    Remove = 4
}

public sealed record AiIntent(
    AiAction Action,
    IReadOnlyDictionary<string, string> Fields,
    DateOnly? From = null,
    DateOnly? To = null,
    bool AsPdf = false,
    string? Reply = null);

public sealed record AiContext(
    DateOnly Today,
    IReadOnlyList<KnownCard> Cards,
    SessionDraft Draft,
    MenuStep Step);

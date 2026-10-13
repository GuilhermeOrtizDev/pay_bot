using System.Globalization;
using PayBot.Domain;

namespace PayBot.Menu;

public static class MenuEngine
{
    public static MenuTurn Handle(
        string? rawText,
        MenuStep step,
        SessionDraft current,
        IReadOnlyList<KnownCard> cards,
        DateOnly today)
    {
        var draft = current.Clone();
        var text = (rawText ?? "").Trim();

        if (text == "0" && step != MenuStep.Main)
            return OpenMain();

        return step switch
        {
            MenuStep.Main => OnMain(text),
            MenuStep.ExpenseMenu => OnExpense(text, draft),
            MenuStep.AwaitingAmount => OnAmount(text, draft, cards),
            MenuStep.AwaitingInstallments => OnInstallments(text, draft, cards),
            MenuStep.AwaitingCardChoice => OnCardChoice(text, draft, cards),
            MenuStep.AwaitingNewCardName => OnNewCard(text, draft, cards),
            MenuStep.AwaitingIncomeAmount => OnIncomeAmount(text, draft),
            MenuStep.AwaitingIncomeDescription => OnIncomeDescription(text, draft),
            MenuStep.SummaryMenu => OnSummary(text, today),
            MenuStep.AwaitingDateRange => OnDateRange(text),
            _ => OpenMain()
        };
    }

    private static MenuTurn OnMain(string text) => text switch
    {
        "1" => new MenuTurn(MenuTexts.Expense, MenuStep.ExpenseMenu, new SessionDraft(), null, null, null),
        "2" => new MenuTurn(MenuTexts.AskAmount, MenuStep.AwaitingIncomeAmount, new SessionDraft(), null, null, null),
        "3" => new MenuTurn(MenuTexts.Summary, MenuStep.SummaryMenu, new SessionDraft(), null, null, null),
        _ => OpenMain()
    };

    private static MenuTurn OnExpense(string text, SessionDraft draft)
    {
        var method = text switch
        {
            "1" => PaymentMethod.Pix,
            "2" => PaymentMethod.CreditCard,
            "3" => PaymentMethod.DebitCard,
            "4" => PaymentMethod.CashWithdrawal,
            "5" => PaymentMethod.Fixed,
            "6" => PaymentMethod.Other,
            _ => (PaymentMethod?)null
        };

        if (method is null)
            return Stay(MenuStep.ExpenseMenu, draft, MenuTexts.Expense);

        draft.Method = method;
        draft.Amount = null;
        draft.Installments = null;
        return Stay(MenuStep.AwaitingAmount, draft, MenuTexts.AskAmount);
    }

    private static MenuTurn OnAmount(string text, SessionDraft draft, IReadOnlyList<KnownCard> cards)
    {
        if (draft.Method is null)
            return OpenMain();
        if (!MoneyParser.TryParse(text, out var amount))
            return Stay(MenuStep.AwaitingAmount, draft, MenuTexts.InvalidAmount);

        draft.Amount = amount;
        if (draft.Method == PaymentMethod.CreditCard)
            return Stay(MenuStep.AwaitingInstallments, draft, MenuTexts.AskInstallments);
        if (draft.Method == PaymentMethod.DebitCard)
            return CardPrompt(draft, cards);

        return FinishExpense(draft, null, null, false);
    }

    private static MenuTurn OnInstallments(string text, SessionDraft draft, IReadOnlyList<KnownCard> cards)
    {
        if (draft.Method != PaymentMethod.CreditCard || draft.Amount is null)
            return OpenMain();
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count is < 1 or > 480)
            return Stay(MenuStep.AwaitingInstallments, draft, MenuTexts.InvalidInstallments);

        draft.Installments = count;
        return CardPrompt(draft, cards);
    }

    private static MenuTurn OnCardChoice(string text, SessionDraft draft, IReadOnlyList<KnownCard> cards)
    {
        if (!TryCardContext(draft, cards, out var list))
            return OpenMain();
        if (text == "99")
            return Stay(MenuStep.AwaitingNewCardName, draft, MenuTexts.AskCardName);
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index >= 1 && index <= list.Count)
        {
            var card = list[index - 1];
            return FinishExpense(draft, card.Id, card.Name, false);
        }

        return Stay(MenuStep.AwaitingCardChoice, draft, MenuTexts.CardList(list));
    }

    private static MenuTurn OnNewCard(string text, SessionDraft draft, IReadOnlyList<KnownCard> cards)
    {
        if (!TryCardContext(draft, cards, out var list))
            return OpenMain();

        var name = text.Trim();
        if (name.Length is 0 or > 80)
            return Stay(MenuStep.AwaitingNewCardName, draft, MenuTexts.InvalidCardName);

        var existing = list.FirstOrDefault(card => string.Equals(card.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return FinishExpense(draft, existing.Id, existing.Name, false);

        return FinishExpense(draft, null, name, true);
    }

    private static MenuTurn OnIncomeAmount(string text, SessionDraft draft)
    {
        if (!MoneyParser.TryParse(text, out var amount))
            return Stay(MenuStep.AwaitingIncomeAmount, draft, MenuTexts.InvalidAmount);

        draft.Amount = amount;
        return Stay(MenuStep.AwaitingIncomeDescription, draft, MenuTexts.AskDescription);
    }

    private static MenuTurn OnIncomeDescription(string text, SessionDraft draft)
    {
        if (draft.Amount is null)
            return OpenMain();

        var description = text.Trim();
        if (description.Length is 0 or > 200)
            return Stay(MenuStep.AwaitingIncomeDescription, draft, MenuTexts.InvalidDescription);

        var income = new PendingIncome(draft.Amount.Value, description);
        var reply = $"Entrada registrada: {MenuTexts.Money(income.Amount)} - {description}\n\n{MenuTexts.Main}";
        return new MenuTurn(reply, MenuStep.Main, new SessionDraft(), null, income, null);
    }

    private static MenuTurn OnSummary(string text, DateOnly today) => text switch
    {
        "1" => Period(StartOfMonth(today), EndOfMonth(today)),
        "2" => Period(new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31)),
        "3" => new MenuTurn("", MenuStep.SummaryMenu, new SessionDraft(), null, null, new SummaryQuery(null, null, "Resumo total")),
        "4" => Stay(MenuStep.AwaitingDateRange, new SessionDraft(), MenuTexts.AskPeriod),
        _ => Stay(MenuStep.SummaryMenu, new SessionDraft(), MenuTexts.Summary)
    };

    private static MenuTurn OnDateRange(string text)
    {
        if (!DateRangeParser.TryParse(text, out var from, out var to))
            return Stay(MenuStep.AwaitingDateRange, new SessionDraft(), MenuTexts.InvalidPeriod);

        return Period(from, to);
    }

    private static MenuTurn Period(DateOnly from, DateOnly to)
    {
        var title = $"Resumo de {MenuTexts.FormatDate(from)} a {MenuTexts.FormatDate(to)}";
        return new MenuTurn("", MenuStep.SummaryMenu, new SessionDraft(), null, null, new SummaryQuery(from, to, title));
    }

    private static MenuTurn CardPrompt(SessionDraft draft, IReadOnlyList<KnownCard> cards)
    {
        if (!TryCardContext(draft, cards, out var list))
            return OpenMain();
        return Stay(MenuStep.AwaitingCardChoice, draft, MenuTexts.CardList(list));
    }

    private static MenuTurn FinishExpense(SessionDraft draft, Guid? cardId, string? cardName, bool createCard)
    {
        var method = draft.Method!.Value;
        var amount = draft.Amount!.Value;
        var installments = method == PaymentMethod.CreditCard ? draft.Installments : null;
        var expense = new PendingExpense(method, amount, installments, cardId, cardName, createCard);
        var cardPart = string.IsNullOrWhiteSpace(cardName) ? "" : $" {cardName.Trim()}";
        var parcel = installments is > 0 ? $" em {installments}x" : "";
        var reply = $"Gasto registrado: {MenuTexts.MethodLabel(method)}{cardPart} {MenuTexts.Money(amount)}{parcel}\n\n{MenuTexts.Main}";
        return new MenuTurn(reply, MenuStep.Main, new SessionDraft(), expense, null, null);
    }

    private static bool TryCardContext(SessionDraft draft, IReadOnlyList<KnownCard> cards, out IReadOnlyList<KnownCard> list)
    {
        list = [];
        if (draft.Amount is null)
            return false;

        var kind = draft.Method switch
        {
            PaymentMethod.CreditCard => CardKind.Credit,
            PaymentMethod.DebitCard => CardKind.Debit,
            _ => (CardKind?)null
        };
        if (kind is null)
            return false;

        list = cards.Where(card => card.Kind == kind).Take(98).ToList();
        return true;
    }

    private static DateOnly StartOfMonth(DateOnly today) => new(today.Year, today.Month, 1);

    private static DateOnly EndOfMonth(DateOnly today) => StartOfMonth(today).AddMonths(1).AddDays(-1);

    private static MenuTurn OpenMain() =>
        new(MenuTexts.Main, MenuStep.Main, new SessionDraft(), null, null, null);

    private static MenuTurn Stay(MenuStep step, SessionDraft draft, string reply) =>
        new(reply, step, draft, null, null, null);
}

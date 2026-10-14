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

        if (step == MenuStep.Paused)
            return text.Equals("ativar bot", StringComparison.OrdinalIgnoreCase) ? Resume() : StayPaused();

        if (text == "0" && step != MenuStep.Main)
            return OpenMain();

        return step switch
        {
            MenuStep.Main => OnMain(text),
            MenuStep.ExpenseMenu => OnExpense(text, draft),
            MenuStep.AwaitingCreditCharge => OnCreditCharge(text, draft),
            MenuStep.AwaitingDebitCharge => OnDebitCharge(text, draft),
            MenuStep.AwaitingAmount => OnAmount(text, draft, cards),
            MenuStep.AwaitingInstallments => OnInstallments(text, draft),
            MenuStep.AwaitingCardChoice => OnCardChoice(text, draft, cards),
            MenuStep.AwaitingNewCardName => OnNewCard(text, draft, cards),
            MenuStep.AwaitingExpenseDescription => OnExpenseDescription(text, draft),
            MenuStep.AwaitingChargeMonth => OnChargeMonth(text, draft),
            MenuStep.AwaitingIncomeAmount => OnIncomeAmount(text, draft),
            MenuStep.AwaitingIncomeDescription => OnIncomeDescription(text, draft),
            MenuStep.AwaitingIncomeMonth => OnIncomeMonth(text, draft),
            MenuStep.SummaryMenu => OnSummary(text, today),
            MenuStep.AwaitingSummaryOutput => OnSummaryOutput(text, draft),
            MenuStep.AwaitingDateRange => OnDateRange(text),
            MenuStep.RemoveMenu => OnRemove(text),
            MenuStep.AwaitingDeleteDateRange => OnDeleteDateRange(text),
            MenuStep.AwaitingDeleteChoice => OnDeleteChoice(text, draft),
            _ => OpenMain()
        };
    }

    private static MenuTurn OnMain(string text) => text switch
    {
        "1" => new MenuTurn(MenuTexts.Expense, MenuStep.ExpenseMenu, new SessionDraft(), null, null, null),
        "2" => new MenuTurn(MenuTexts.AskAmount, MenuStep.AwaitingIncomeAmount, new SessionDraft(), null, null, null),
        "3" => new MenuTurn(MenuTexts.Summary, MenuStep.SummaryMenu, new SessionDraft(), null, null, null),
        "4" => new MenuTurn(MenuTexts.Remove, MenuStep.RemoveMenu, new SessionDraft(), null, null, null),
        "5" => Pause(),
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
        draft.Billing = null;
        draft.CardId = null;
        draft.CardName = null;
        draft.CreateCard = false;
        if (method == PaymentMethod.CreditCard)
            return Stay(MenuStep.AwaitingCreditCharge, draft, MenuTexts.CreditCharge);
        if (method == PaymentMethod.DebitCard)
            return Stay(MenuStep.AwaitingDebitCharge, draft, MenuTexts.DebitCharge);

        return Stay(MenuStep.AwaitingAmount, draft, MenuTexts.AskAmount);
    }

    private static MenuTurn OnCreditCharge(string text, SessionDraft draft)
    {
        if (draft.Method != PaymentMethod.CreditCard)
            return OpenMain();

        var billing = text switch
        {
            "1" => CardBilling.Installments,
            "2" => CardBilling.InFull,
            "3" => CardBilling.Fixed,
            _ => (CardBilling?)null
        };
        if (billing is null)
            return Stay(MenuStep.AwaitingCreditCharge, draft, MenuTexts.CreditCharge);

        draft.Billing = billing;
        draft.Installments = null;
        if (billing == CardBilling.Installments)
            return Stay(MenuStep.AwaitingInstallments, draft, MenuTexts.AskInstallments);

        return Stay(MenuStep.AwaitingAmount, draft, MenuTexts.AskAmount);
    }

    private static MenuTurn OnDebitCharge(string text, SessionDraft draft)
    {
        if (draft.Method != PaymentMethod.DebitCard)
            return OpenMain();

        var billing = text switch
        {
            "1" => CardBilling.InFull,
            "2" => CardBilling.Fixed,
            _ => (CardBilling?)null
        };
        if (billing is null)
            return Stay(MenuStep.AwaitingDebitCharge, draft, MenuTexts.DebitCharge);

        draft.Billing = billing;
        return Stay(MenuStep.AwaitingAmount, draft, MenuTexts.AskAmount);
    }

    private static MenuTurn OnAmount(string text, SessionDraft draft, IReadOnlyList<KnownCard> cards)
    {
        if (draft.Method is null)
            return OpenMain();
        if (!MoneyParser.TryParse(text, out var amount))
            return Stay(MenuStep.AwaitingAmount, draft, MenuTexts.InvalidAmount);

        draft.Amount = amount;
        if (draft.Method is PaymentMethod.CreditCard or PaymentMethod.DebitCard)
            return CardPrompt(draft, cards);

        return Stay(MenuStep.AwaitingExpenseDescription, draft, MenuTexts.AskDescription);
    }

    private static MenuTurn OnInstallments(string text, SessionDraft draft)
    {
        if (draft.Method != PaymentMethod.CreditCard || draft.Billing != CardBilling.Installments)
            return OpenMain();
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count is < 1 or > 480)
            return Stay(MenuStep.AwaitingInstallments, draft, MenuTexts.InvalidInstallments);

        draft.Installments = count;
        return Stay(MenuStep.AwaitingAmount, draft, MenuTexts.AskAmount);
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
            return RememberCard(draft, card.Id, card.Name, false);
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
            return RememberCard(draft, existing.Id, existing.Name, false);

        return RememberCard(draft, null, name, true);
    }

    private static MenuTurn OnExpenseDescription(string text, SessionDraft draft)
    {
        if (draft.Method is null || draft.Amount is null)
            return OpenMain();
        if (draft.Method is PaymentMethod.CreditCard or PaymentMethod.DebitCard && string.IsNullOrWhiteSpace(draft.CardName))
            return OpenMain();

        var description = text.Trim();
        if (description.Length is 0 or > 200)
            return Stay(MenuStep.AwaitingExpenseDescription, draft, MenuTexts.InvalidDescription);

        draft.Description = description;
        if (NeedsChargeMonth(draft.Method))
            return Stay(MenuStep.AwaitingChargeMonth, draft, MenuTexts.AskChargeMonth);

        return FinishExpense(draft);
    }

    private static MenuTurn OnChargeMonth(string text, SessionDraft draft)
    {
        if (draft.Method is null || draft.Amount is null || string.IsNullOrWhiteSpace(draft.Description))
            return OpenMain();
        if (!TryParseMonth(text, out var month))
            return Stay(MenuStep.AwaitingChargeMonth, draft, MenuTexts.InvalidChargeMonth);

        draft.ChargeMonth = month;
        return FinishExpense(draft);
    }

    private static bool NeedsChargeMonth(PaymentMethod? method) =>
        method is PaymentMethod.CreditCard or PaymentMethod.Fixed or PaymentMethod.Other;

    private static bool TryParseMonth(string text, out DateOnly month)
    {
        month = default;
        var formats = new[] { "MM/yyyy", "M/yyyy" };
        if (!DateOnly.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;

        month = new DateOnly(parsed.Year, parsed.Month, 1);
        return true;
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

        draft.Description = description;
        return Stay(MenuStep.AwaitingIncomeMonth, draft, MenuTexts.AskChargeMonth);
    }

    private static MenuTurn OnIncomeMonth(string text, SessionDraft draft)
    {
        if (draft.Amount is null || string.IsNullOrWhiteSpace(draft.Description))
            return OpenMain();
        if (!TryParseMonth(text, out var month))
            return Stay(MenuStep.AwaitingIncomeMonth, draft, MenuTexts.InvalidChargeMonth);

        draft.ChargeMonth = month;
        var description = draft.Description.Trim();
        var income = new PendingIncome(draft.Amount.Value, description, month);
        var start = month.ToString("MM/yyyy", CultureInfo.InvariantCulture);
        var reply = $"Entrada registrada: {MenuTexts.Money(income.Amount)} a partir de {start} - {description}\n\n{MenuTexts.Main}";
        return new MenuTurn(reply, MenuStep.Main, new SessionDraft(), null, income, null);
    }

    private static MenuTurn OnSummary(string text, DateOnly today) => text switch
    {
        "1" => Period(StartOfMonth(today), EndOfMonth(today)),
        "2" => Period(StartOfMonth(today.AddMonths(1)), EndOfMonth(today.AddMonths(1))),
        "3" => Period(new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31)),
        "4" => AskFormat(new SummaryQuery(null, null, "Resumo total")),
        "5" => Stay(MenuStep.AwaitingDateRange, new SessionDraft(), MenuTexts.AskMonthPeriod),
        _ => Stay(MenuStep.SummaryMenu, new SessionDraft(), MenuTexts.Summary)
    };

    private static MenuTurn OnRemove(string text) => text switch
    {
        "1" => new MenuTurn("", MenuStep.AwaitingDeleteChoice, new SessionDraft(), null, null, null, new DeleteListRequest(null, null, 10)),
        "2" => Stay(MenuStep.AwaitingDeleteDateRange, new SessionDraft(), MenuTexts.AskPeriod),
        _ => Stay(MenuStep.RemoveMenu, new SessionDraft(), MenuTexts.Remove)
    };

    private static MenuTurn OnDeleteDateRange(string text)
    {
        if (!DateRangeParser.TryParse(text, out var from, out var to))
            return Stay(MenuStep.AwaitingDeleteDateRange, new SessionDraft(), MenuTexts.InvalidPeriod);

        return new MenuTurn(
            "",
            MenuStep.AwaitingDeleteChoice,
            new SessionDraft(),
            null,
            null,
            null,
            new DeleteListRequest(from, to, 30));
    }

    private static MenuTurn OnDeleteChoice(string text, SessionDraft draft)
    {
        var ids = draft.DeleteIds ?? [];
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            && index >= 1
            && index <= ids.Count)
        {
            return new MenuTurn("", MenuStep.RemoveMenu, new SessionDraft(), null, null, null, null, ids[index - 1]);
        }

        return Stay(MenuStep.AwaitingDeleteChoice, draft, MenuTexts.InvalidDeleteId);
    }

    private static MenuTurn OnDateRange(string text)
    {
        if (!DateRangeParser.TryParseMonths(text, out var from, out var to))
            return Stay(MenuStep.AwaitingDateRange, new SessionDraft(), MenuTexts.InvalidMonthPeriod);

        var fromLabel = from.ToString("MM/yyyy", CultureInfo.InvariantCulture);
        var toLabel = to.ToString("MM/yyyy", CultureInfo.InvariantCulture);
        var title = fromLabel == toLabel ? $"Resumo de {fromLabel}" : $"Resumo de {fromLabel} a {toLabel}";
        return AskFormat(new SummaryQuery(from, to, title));
    }

    private static MenuTurn OnSummaryOutput(string text, SessionDraft draft)
    {
        var query = QueryFrom(draft);
        if (query is null)
            return OpenMain();

        return text switch
        {
            "1" => new MenuTurn("", MenuStep.SummaryMenu, new SessionDraft(), null, null, query, AsPdf: true),
            "2" => new MenuTurn("", MenuStep.SummaryMenu, new SessionDraft(), null, null, query),
            _ => Stay(MenuStep.AwaitingSummaryOutput, draft, MenuTexts.SummaryOutput)
        };
    }

    private static MenuTurn Period(DateOnly from, DateOnly to)
    {
        var title = $"Resumo de {MenuTexts.FormatDate(from)} a {MenuTexts.FormatDate(to)}";
        return AskFormat(new SummaryQuery(from, to, title));
    }

    private static MenuTurn AskFormat(SummaryQuery query)
    {
        var draft = new SessionDraft
        {
            SummaryFrom = query.From,
            SummaryTo = query.To,
            SummaryTitle = query.Title
        };
        return Stay(MenuStep.AwaitingSummaryOutput, draft, MenuTexts.SummaryOutput);
    }

    private static SummaryQuery? QueryFrom(SessionDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.SummaryTitle))
            return null;
        return new SummaryQuery(draft.SummaryFrom, draft.SummaryTo, draft.SummaryTitle);
    }

    private static MenuTurn CardPrompt(SessionDraft draft, IReadOnlyList<KnownCard> cards)
    {
        if (!TryCardContext(draft, cards, out var list))
            return OpenMain();
        return Stay(MenuStep.AwaitingCardChoice, draft, MenuTexts.CardList(list));
    }

    private static MenuTurn RememberCard(SessionDraft draft, Guid? cardId, string? cardName, bool createCard)
    {
        draft.CardId = cardId;
        draft.CardName = cardName;
        draft.CreateCard = createCard;
        return Stay(MenuStep.AwaitingExpenseDescription, draft, MenuTexts.AskDescription);
    }

    private static MenuTurn FinishExpense(SessionDraft draft)
    {
        var method = draft.Method!.Value;
        var amount = draft.Amount!.Value;
        var description = draft.Description!.Trim();
        var installments = method == PaymentMethod.CreditCard && draft.Billing == CardBilling.Installments
            ? draft.Installments
            : null;
        var isFixed = draft.Billing == CardBilling.Fixed;
        var expense = new PendingExpense(
            method,
            amount,
            installments,
            draft.CardId,
            draft.CardName,
            draft.CreateCard,
            description,
            isFixed,
            draft.ChargeMonth);
        var cardPart = string.IsNullOrWhiteSpace(draft.CardName) ? "" : $" {draft.CardName.Trim()}";
        var billing = BillingLabel(method, draft.Billing, installments);
        var start = draft.ChargeMonth is DateOnly month
            ? $" a partir de {month.ToString("MM/yyyy", CultureInfo.InvariantCulture)}"
            : "";
        var reply = $"Gasto registrado: {MenuTexts.MethodLabel(method)}{cardPart} {MenuTexts.Money(amount)}{billing}{start} - {description}\n\n{MenuTexts.Main}";
        return new MenuTurn(reply, MenuStep.Main, new SessionDraft(), expense, null, null);
    }

    private static string BillingLabel(PaymentMethod method, CardBilling? billing, int? installments)
    {
        if (method == PaymentMethod.CreditCard && billing == CardBilling.Installments && installments is > 0)
            return $" em {installments}x";
        if (billing == CardBilling.InFull)
            return " a vista";
        if (billing == CardBilling.Fixed)
            return " fixo";
        return "";
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

    private static MenuTurn Pause() =>
        new(MenuTexts.Paused, MenuStep.Paused, new SessionDraft(), null, null, null);

    private static MenuTurn StayPaused() =>
        new("", MenuStep.Paused, new SessionDraft(), null, null, null);

    private static MenuTurn Resume() =>
        new(MenuTexts.Resumed + "\n\n" + MenuTexts.Main, MenuStep.Main, new SessionDraft(), null, null, null);

    private static MenuTurn Stay(MenuStep step, SessionDraft draft, string reply) =>
        new(reply, step, draft, null, null, null);
}

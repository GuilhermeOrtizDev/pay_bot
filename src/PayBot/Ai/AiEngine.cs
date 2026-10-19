using System.Globalization;
using PayBot.Domain;
using PayBot.Menu;

namespace PayBot.Ai;

public static class AiEngine
{
    public static bool IsAiStep(MenuStep step) =>
        step is MenuStep.AiMode
            or MenuStep.AiAwaitingConfirm
            or MenuStep.AiAwaitingDeleteChoice
            or MenuStep.AiAwaitingDeleteConfirm;

    public static MenuTurn? Direct(
        string? rawText,
        MenuStep step,
        SessionDraft current,
        IReadOnlyList<KnownCard> cards)
    {
        if (AdminEngine.IsCommand(rawText))
            return Stay(step, current, Reminder(null, step));

        var text = Fold(rawText);
        if (text == "desativar ia")
            return Exit();

        switch (step)
        {
            case MenuStep.AiAwaitingConfirm:
                if (IsYes(text))
                    return Save(current, cards);
                if (IsNo(text))
                    return Cancel();
                return null;

            case MenuStep.AiAwaitingDeleteChoice:
                if (IsNo(text) || text == "0")
                    return Cancel();
                if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                    return null;
                var ids = current.DeleteIds ?? [];
                if (index < 1 || index > ids.Count)
                    return Stay(step, current, MenuTexts.InvalidDeleteId + "\n" + MenuTexts.AiDeleteChoice);
                var chosen = current.Clone();
                chosen.DeleteIds = [ids[index - 1]];
                return Stay(MenuStep.AiAwaitingDeleteConfirm, chosen, MenuTexts.AiDeleteConfirm(index));

            case MenuStep.AiAwaitingDeleteConfirm:
                if (IsYes(text) && current.DeleteIds is [var id])
                    return new MenuTurn("", MenuStep.AiMode, new SessionDraft(), null, null, null, DeleteId: id);
                if (IsNo(text))
                    return Cancel();
                return null;

            default:
                return null;
        }
    }

    public static MenuTurn Interpret(
        AiIntent intent,
        MenuStep step,
        SessionDraft current,
        IReadOnlyList<KnownCard> cards)
    {
        return intent.Action switch
        {
            AiAction.Expense => Register(intent, step, current, cards, isIncome: false),
            AiAction.Income => Register(intent, step, current, cards, isIncome: true),
            AiAction.Summary => Summary(intent),
            AiAction.Remove => Remove(intent),
            _ => Stay(step, current, Reminder(intent.Reply, step))
        };
    }

    public static MenuTurn Stay(MenuStep step, SessionDraft draft, string reply) =>
        new(reply, step, draft, null, null, null);

    public static bool HasPendingEntry(MenuStep step, SessionDraft draft) =>
        step == MenuStep.AiAwaitingConfirm
        || (step == MenuStep.AiMode
            && (draft.IsBulkIncome
                || draft.Method is not null
                || draft.Amount is not null
                || draft.Installments is not null
                || draft.Billing is not null
                || !string.IsNullOrWhiteSpace(draft.CardName)
                || !string.IsNullOrWhiteSpace(draft.Description)
                || draft.ChargeMonth is not null));

    public static IReadOnlyDictionary<string, string> DraftFields(SessionDraft draft)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (draft.Method is PaymentMethod method)
            fields["tipo"] = MethodField(method);
        if (draft.Billing is CardBilling billing)
            fields["cobranca"] = BillingField(billing);
        if (draft.Installments is int installments)
            fields["parcelas"] = installments.ToString(CultureInfo.InvariantCulture);
        if (draft.Amount is decimal amount)
            fields["valor"] = amount.ToString("0.00", MenuTexts.PtBr);
        if (!string.IsNullOrWhiteSpace(draft.CardName))
            fields["cartao"] = draft.CardName.Trim();
        if (!string.IsNullOrWhiteSpace(draft.Description))
            fields["descricao"] = draft.Description.Trim();
        if (draft.ChargeMonth is DateOnly month)
            fields["mes"] = month.ToString("MM/yyyy", CultureInfo.InvariantCulture);
        return fields;
    }

    private static MenuTurn Register(
        AiIntent intent,
        MenuStep step,
        SessionDraft current,
        IReadOnlyList<KnownCard> cards,
        bool isIncome)
    {
        var start = HasPendingEntry(step, current) ? current : new SessionDraft();
        var read = BulkEntry.FromFields(intent.Fields, start, isIncome, cards);
        if (read.Problems.Count > 0)
            return Stay(MenuStep.AiMode, read.Draft, MenuTexts.AiMissing(read.Problems));

        var line = isIncome
            ? "Entrada " + MenuEngine.IncomeLine(read.Draft)
            : "Gasto " + MenuEngine.ExpenseLine(read.Draft);
        return Stay(MenuStep.AiAwaitingConfirm, read.Draft, MenuTexts.AiConfirm(line));
    }

    private static MenuTurn Save(SessionDraft current, IReadOnlyList<KnownCard> cards)
    {
        var read = BulkEntry.FromFields(new Dictionary<string, string>(), current, current.IsBulkIncome, cards);
        if (read.Problems.Count > 0)
            return Stay(MenuStep.AiMode, read.Draft, MenuTexts.AiMissing(read.Problems));

        if (read.Draft.IsBulkIncome)
        {
            var reply = "Entrada registrada: " + MenuEngine.IncomeLine(read.Draft);
            return new MenuTurn(reply, MenuStep.AiMode, new SessionDraft(), null, MenuEngine.ToIncome(read.Draft), null);
        }

        var saved = "Gasto registrado: " + MenuEngine.ExpenseLine(read.Draft);
        return new MenuTurn(saved, MenuStep.AiMode, new SessionDraft(), MenuEngine.ToExpense(read.Draft), null, null);
    }

    private static MenuTurn Summary(AiIntent intent)
    {
        var query = Range(intent) is { } range
            ? new SummaryQuery(
                range.From,
                range.To,
                $"Resumo de {MenuTexts.FormatDate(range.From)} a {MenuTexts.FormatDate(range.To)}")
            : new SummaryQuery(null, null, "Resumo total");

        return new MenuTurn("", MenuStep.AiMode, new SessionDraft(), null, null, query, AsPdf: intent.AsPdf);
    }

    private static MenuTurn Remove(AiIntent intent)
    {
        var request = Range(intent) is { } range
            ? new DeleteListRequest(range.From, range.To, 30)
            : new DeleteListRequest(null, null, 10);
        return new MenuTurn("", MenuStep.AiAwaitingDeleteChoice, new SessionDraft(), null, null, null, request);
    }

    private static (DateOnly From, DateOnly To)? Range(AiIntent intent)
    {
        if (intent.From is null && intent.To is null)
            return null;

        var from = intent.From ?? intent.To!.Value;
        var to = intent.To ?? intent.From!.Value;
        return from <= to ? (from, to) : (to, from);
    }

    private static string Reminder(string? reply, MenuStep step)
    {
        var text = string.IsNullOrWhiteSpace(reply) ? MenuTexts.AiHelp : reply.Trim();
        return step switch
        {
            MenuStep.AiAwaitingConfirm or MenuStep.AiAwaitingDeleteConfirm => text + "\nResponda sim ou nao.",
            MenuStep.AiAwaitingDeleteChoice => text + "\n" + MenuTexts.AiDeleteChoice,
            _ => text
        };
    }

    private static MenuTurn Exit() =>
        new(MenuTexts.AiModeOff + "\n\n" + MenuTexts.MainMenu(true), MenuStep.Main, new SessionDraft(), null, null, null);

    private static MenuTurn Cancel() => Stay(MenuStep.AiMode, new SessionDraft(), MenuTexts.AiCancelled);

    private static bool IsYes(string text) =>
        text is "sim" or "s" or "ok" or "confirmo" or "confirmar" or "confirma" or "pode" or "isso" or "sim pode";

    private static bool IsNo(string text) =>
        text is "nao" or "n" or "cancelar" or "cancela" or "nao quero";

    private static string Fold(string? text) =>
        BulkEntry.Fold(text ?? "").TrimEnd('.', '!', ',', ';');

    private static string MethodField(PaymentMethod method) => method switch
    {
        PaymentMethod.Pix => "pix",
        PaymentMethod.CreditCard => "cartao credito",
        PaymentMethod.DebitCard => "cartao debito",
        PaymentMethod.CashWithdrawal => "retirada",
        PaymentMethod.Fixed => "despesa fixa",
        _ => "outros"
    };

    private static string BillingField(CardBilling billing) => billing switch
    {
        CardBilling.Installments => "parcelas",
        CardBilling.InFull => "a vista",
        _ => "fixo"
    };
}

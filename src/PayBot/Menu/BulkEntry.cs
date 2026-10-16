using System.Globalization;
using System.Text;
using PayBot.Domain;

namespace PayBot.Menu;

public static class BulkEntry
{
    public sealed record Read(SessionDraft Draft, IReadOnlyList<string> Problems);

    public static bool IsBlock(string text)
    {
        foreach (var line in Lines(text))
            return IsKind(line);
        return false;
    }

    public static Read ReadBlock(string text, IReadOnlyList<KnownCard> cards)
    {
        var lines = Lines(text);
        var draft = new SessionDraft { IsBulkIncome = lines.Count > 0 && Fold(lines[0]) == "entrada" };
        Apply(draft, Fields(lines, skipKindHeader: true));
        return Finish(draft, cards);
    }

    public static Read ReadPatch(string text, SessionDraft current, IReadOnlyList<KnownCard> cards)
    {
        var draft = current.Clone();
        var fields = Fields(Lines(text), skipKindHeader: false);
        if (fields.Count > 0)
            Apply(draft, fields);
        return Finish(draft, cards);
    }

    private static Read Finish(SessionDraft draft, IReadOnlyList<KnownCard> cards)
    {
        Normalize(draft, cards);
        return new Read(draft, Problems(draft));
    }

    private static void Apply(SessionDraft draft, Dictionary<string, string> fields)
    {
        if (fields.TryGetValue("tipo", out var tipo))
            draft.Method = ParseMethod(tipo);
        if (fields.TryGetValue("cobranca", out var billing))
            draft.Billing = ParseBilling(billing);
        if (fields.TryGetValue("parcelas", out var installments))
            draft.Installments = ParseInstallments(installments);
        if (fields.TryGetValue("valor", out var amount))
            draft.Amount = MoneyParser.TryParse(amount, out var parsed) ? parsed : null;
        if (fields.TryGetValue("cartao", out var card))
            draft.CardName = ParseCardName(card);
        if (fields.TryGetValue("descricao", out var description))
            draft.Description = ParseDescription(description);
        if (fields.TryGetValue("mes", out var month))
            draft.ChargeMonth = DateRangeParser.TryParseMonth(month, out var parsed) ? parsed : null;
    }

    private static void Normalize(SessionDraft draft, IReadOnlyList<KnownCard> cards)
    {
        if (draft.IsBulkIncome)
        {
            draft.Method = null;
            draft.Billing = null;
            draft.Installments = null;
            ClearCard(draft);
            return;
        }

        if (draft.Method == PaymentMethod.DebitCard && draft.Billing == CardBilling.Installments)
            draft.Billing = null;

        if (draft.Method is PaymentMethod.CreditCard or PaymentMethod.DebitCard)
        {
            if (draft.Billing != CardBilling.Installments)
                draft.Installments = null;
            if (draft.Method == PaymentMethod.DebitCard)
                draft.ChargeMonth = null;
            ResolveCard(draft, cards);
            return;
        }

        if (draft.Method is not null)
        {
            draft.Billing = null;
            draft.Installments = null;
            ClearCard(draft);
            if (!NeedsMonth(draft.Method.Value))
                draft.ChargeMonth = null;
        }
    }

    private static List<string> Problems(SessionDraft draft)
    {
        var problems = new List<string>();
        if (draft.IsBulkIncome)
        {
            if (draft.Amount is null)
                problems.Add("valor");
            if (string.IsNullOrWhiteSpace(draft.Description))
                problems.Add("descricao");
            if (draft.ChargeMonth is null)
                problems.Add("mes");
            return problems;
        }

        if (draft.Method is null)
            problems.Add("tipo");

        var card = draft.Method is PaymentMethod.CreditCard or PaymentMethod.DebitCard;
        if (card && draft.Billing is null)
            problems.Add("cobranca");
        if (draft.Billing == CardBilling.Installments && draft.Installments is null)
            problems.Add("parcelas");
        if (draft.Amount is null)
            problems.Add("valor");
        if (card && string.IsNullOrWhiteSpace(draft.CardName))
            problems.Add("cartao");
        if (string.IsNullOrWhiteSpace(draft.Description))
            problems.Add("descricao");
        if (draft.Method is PaymentMethod method && NeedsMonth(method) && draft.ChargeMonth is null)
            problems.Add("mes");
        return problems;
    }

    private static void ResolveCard(SessionDraft draft, IReadOnlyList<KnownCard> cards)
    {
        var name = draft.CardName?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            ClearCard(draft);
            return;
        }

        var kind = draft.Method == PaymentMethod.CreditCard ? CardKind.Credit : CardKind.Debit;
        var existing = cards.FirstOrDefault(card =>
            card.Kind == kind && string.Equals(card.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            draft.CardId = null;
            draft.CardName = name;
            draft.CreateCard = true;
            return;
        }

        draft.CardId = existing.Id;
        draft.CardName = existing.Name;
        draft.CreateCard = false;
    }

    private static void ClearCard(SessionDraft draft)
    {
        draft.CardId = null;
        draft.CardName = null;
        draft.CreateCard = false;
    }

    private static bool NeedsMonth(PaymentMethod method) =>
        method is PaymentMethod.CreditCard or PaymentMethod.Fixed or PaymentMethod.Other;

    private static PaymentMethod? ParseMethod(string text) => Fold(text) switch
    {
        "pix" => PaymentMethod.Pix,
        "cartao credito" or "cartao de credito" => PaymentMethod.CreditCard,
        "cartao debito" or "cartao de debito" => PaymentMethod.DebitCard,
        "retirada" or "retirada de caixa" => PaymentMethod.CashWithdrawal,
        "despesa fixa" => PaymentMethod.Fixed,
        "outros" => PaymentMethod.Other,
        _ => null
    };

    private static CardBilling? ParseBilling(string text) => Fold(text) switch
    {
        "parcelas" => CardBilling.Installments,
        "a vista" => CardBilling.InFull,
        "fixo" => CardBilling.Fixed,
        _ => null
    };

    private static int? ParseInstallments(string text)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count is < 1 or > 480)
            return null;
        return count;
    }

    private static string? ParseCardName(string text)
    {
        var name = text.Trim();
        return name.Length is 0 or > 80 ? null : name;
    }

    private static string? ParseDescription(string text)
    {
        var description = text.Trim();
        return description.Length is 0 or > 200 ? null : description;
    }

    private static Dictionary<string, string> Fields(List<string> lines, bool skipKindHeader)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < lines.Count; i++)
        {
            if (i == 0 && skipKindHeader)
                continue;
            if (IsKind(lines[i]))
                break;

            var separator = lines[i].IndexOf(':');
            if (separator <= 0)
                continue;

            var key = Fold(lines[i][..separator]);
            if (key is not ("tipo" or "cobranca" or "parcelas" or "valor" or "cartao" or "descricao" or "mes"))
                continue;

            fields[key] = lines[i][(separator + 1)..].Trim();
        }

        return fields;
    }

    private static List<string> Lines(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static bool IsKind(string line)
    {
        var kind = Fold(line);
        return kind is "gasto" or "entrada";
    }

    private static string Fold(string value)
    {
        var lowered = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(lowered.Length);
        var pendingSpace = false;
        foreach (var character in lowered)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}

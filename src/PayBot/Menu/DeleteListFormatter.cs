using System.Text;
using PayBot.Domain;

namespace PayBot.Menu;

public static class DeleteListFormatter
{
    public static string Format(IReadOnlyList<LedgerEntry> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Selecione o id para remover");
        builder.AppendLine("0 - Voltar Menu Inicial");
        builder.Append(FormatChoices(entries));
        return builder.ToString().TrimEnd();
    }

    public static string FormatChoices(IReadOnlyList<LedgerEntry> entries)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < entries.Count; i++)
            builder.AppendLine($"{i + 1} - {Line(entries[i])}");
        return builder.ToString().TrimEnd();
    }

    public static string Line(LedgerEntry entry) =>
        $"{Name(entry)} - {MenuTexts.Money(entry.Amount)} - {MenuTexts.FormatDate(entry.LocalDate)}";

    public static string Name(LedgerEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Description))
            return entry.Description.Trim();
        if (entry.Direction == Direction.Income)
            return "Entrada";
        if (entry.Card is not null && entry.PaymentMethod is PaymentMethod method)
            return $"{MenuTexts.MethodLabel(method)} {entry.Card.Name}";
        if (entry.PaymentMethod is PaymentMethod payment)
            return MenuTexts.MethodLabel(payment);
        return "Registro";
    }
}

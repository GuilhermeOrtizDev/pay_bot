using System.Globalization;
using System.Text;
using PayBot.Domain;
using PayBot.Time;

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

    public static string Line(LedgerEntry entry)
    {
        var reference = entry.LocalDate.ToString("MM/yyyy", CultureInfo.InvariantCulture);
        var generated = MenuTexts.FormatDate(SaoPauloClock.LocalDate(entry.CreatedAt));
        return $"{Name(entry)} - {MenuTexts.Money(entry.Amount)} - {reference} gerado {generated}";
    }

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

    public static string FormatFixed(IReadOnlyList<FixedExpense> entries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Selecione a despesa fixa para encerrar");
        builder.AppendLine("0 - Voltar Menu Inicial");
        builder.Append(FormatFixedChoices(entries));
        return builder.ToString().TrimEnd();
    }

    public static string FormatFixedChoices(IReadOnlyList<FixedExpense> entries)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < entries.Count; i++)
            builder.AppendLine($"{i + 1} - {FixedLine(entries[i])}");
        return builder.ToString().TrimEnd();
    }

    public static string FixedLine(FixedExpense entry)
    {
        var start = entry.StartMonth.ToString("MM/yyyy", CultureInfo.InvariantCulture);
        var name = string.IsNullOrWhiteSpace(entry.Description) ? "Despesa Fixa" : entry.Description.Trim();
        if (entry.EndMonth is DateOnly end)
        {
            var until = end.ToString("MM/yyyy", CultureInfo.InvariantCulture);
            return $"{name} - {MenuTexts.Money(entry.Amount)} - desde {start} ate {until}";
        }

        return $"{name} - {MenuTexts.Money(entry.Amount)} - desde {start}";
    }
}


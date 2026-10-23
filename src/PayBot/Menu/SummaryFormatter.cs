using System.Globalization;
using System.Text;

namespace PayBot.Menu;

public static class SummaryFormatter
{
    private const int LineCap = 40;

    public static string Format(SummaryReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine(report.Title);
        if (!report.ExpensesOnly)
            builder.AppendLine($"Entradas: {MenuTexts.Money(report.IncomeTotal)}");
        builder.AppendLine($"Saidas: {MenuTexts.Money(report.ExpenseTotal)}");
        if (!report.ExpensesOnly)
            builder.AppendLine($"Saldo: {MenuTexts.Money(report.IncomeTotal - report.ExpenseTotal)}");

        if (report.Methods.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Saidas");
            var shown = 0;
            var extra = 0;
            foreach (var method in report.Methods)
            {
                if (shown >= LineCap)
                {
                    extra += 1 + method.Cards.Count;
                    continue;
                }

                builder.AppendLine($"- {MenuTexts.MethodLabel(method.Method)}: {MenuTexts.Money(method.Total)}");
                shown++;
                foreach (var card in method.Cards)
                {
                    if (shown >= LineCap)
                    {
                        extra++;
                        continue;
                    }

                    var parcel = card.Installments is > 0 ? $" em {card.Installments}x" : "";
                    builder.AppendLine($"  - {card.Name}: {MenuTexts.Money(card.Amount)}{parcel}");
                    shown++;
                }
            }

            if (extra > 0)
                builder.AppendLine($"e mais {extra}");
        }

        if (report.Incomes.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Entradas");
            foreach (var line in report.Incomes.Take(LineCap))
            {
                var date = line.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                builder.AppendLine($"- {date} {line.Description}: {MenuTexts.Money(line.Amount)}");
            }

            if (report.Incomes.Count > LineCap)
                builder.AppendLine($"e mais {report.Incomes.Count - LineCap}");
        }

        return builder.ToString().TrimEnd();
    }
}

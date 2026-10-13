using System.Text;

namespace PayBot.Menu;

public static class SummaryPdf
{
    public static byte[] Render(SummaryReport report)
    {
        var lines = new List<string> { report.Title, "" };
        lines.Add($"Entradas: {MenuTexts.Money(report.IncomeTotal)}");
        lines.Add($"Saidas: {MenuTexts.Money(report.ExpenseTotal)}");
        lines.Add($"Saldo: {MenuTexts.Money(report.IncomeTotal - report.ExpenseTotal)}");

        if (report.Methods.Count > 0)
        {
            lines.Add("");
            lines.Add("Saidas");
            foreach (var method in report.Methods)
            {
                lines.Add($"- {MenuTexts.MethodLabel(method.Method)}: {MenuTexts.Money(method.Total)}");
                foreach (var card in method.Cards)
                {
                    var parcel = card.Installments is > 0 ? $" em {card.Installments}x" : "";
                    lines.Add($"  - {card.Name}: {MenuTexts.Money(card.Amount)}{parcel}");
                }
            }
        }

        if (report.Incomes.Count > 0)
        {
            lines.Add("");
            lines.Add("Entradas");
            foreach (var line in report.Incomes)
            {
                var date = MenuTexts.FormatDate(line.Date);
                lines.Add($"- {date} {line.Description}: {MenuTexts.Money(line.Amount)}");
            }
        }

        return SimplePdf.Create(lines);
    }
}

internal static class SimplePdf
{
    private static readonly Encoding Latin1 = Encoding.GetEncoding(
        "iso-8859-1",
        EncoderFallback.ReplacementFallback,
        DecoderFallback.ReplacementFallback);

    public static byte[] Create(IReadOnlyList<string> lines)
    {
        var wrapped = lines.SelectMany(Wrap).ToList();
        if (wrapped.Count == 0)
            wrapped.Add("Resumo");

        var pages = new List<List<string>>();
        for (var index = 0; index < wrapped.Count; index += 42)
            pages.Add(wrapped.GetRange(index, Math.Min(42, wrapped.Count - index)));

        var builder = new StringBuilder();
        var offsets = new List<int>();
        builder.Append("%PDF-1.4\n");

        void WriteObject(int id, string body)
        {
            offsets.Add(builder.Length);
            builder.Append(id).Append(" 0 obj\n").Append(body).Append("\nendobj\n");
        }

        var fontId = 3 + pages.Count * 2;
        var kids = string.Join(' ', Enumerable.Range(0, pages.Count).Select(index => $"{3 + index * 2} 0 R"));
        WriteObject(1, "<< /Type /Catalog /Pages 2 0 R >>");
        WriteObject(2, $"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>");

        for (var index = 0; index < pages.Count; index++)
        {
            var pageId = 3 + index * 2;
            var contentId = pageId + 1;
            var stream = PageContent(pages[index]);
            WriteObject(
                pageId,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents {contentId} 0 R /Resources << /Font << /F1 {fontId} 0 R >> >> >>");
            WriteObject(contentId, $"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream");
        }

        WriteObject(fontId, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");

        var xref = builder.Length;
        builder.Append("xref\n0 ").Append(offsets.Count + 1).Append('\n');
        builder.Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
            builder.Append(offset.ToString("D10")).Append(" 00000 n \n");
        builder.Append("trailer\n<< /Size ").Append(offsets.Count + 1).Append(" /Root 1 0 R >>\n");
        builder.Append("startxref\n").Append(xref).Append("\n%%EOF");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static string PageContent(IReadOnlyList<string> lines)
    {
        var body = new StringBuilder();
        body.Append("BT\n/F1 12 Tf\n40 800 Td\n16 TL\n");
        foreach (var line in lines)
            body.Append(PdfString(line)).Append(" Tj T*\n");
        body.Append("ET");
        return body.ToString();
    }

    private static IEnumerable<string> Wrap(string line)
    {
        const int width = 90;
        if (line.Length == 0)
        {
            yield return "";
            yield break;
        }

        for (var index = 0; index < line.Length; index += width)
            yield return line.Substring(index, Math.Min(width, line.Length - index));
    }

    private static string PdfString(string value)
    {
        var builder = new StringBuilder();
        builder.Append('(');
        foreach (var item in Latin1.GetBytes(value))
        {
            if (item is (byte)'\\' or (byte)'(' or (byte)')')
            {
                builder.Append('\\');
                builder.Append((char)item);
            }
            else if (item is >= 32 and <= 126)
                builder.Append((char)item);
            else
                builder.Append('\\').Append(Convert.ToString(item, 8).PadLeft(3, '0'));
        }

        builder.Append(')');
        return builder.ToString();
    }
}

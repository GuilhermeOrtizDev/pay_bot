using System.Globalization;
using System.Text;
using PayBot.Domain;

namespace PayBot.Menu;

public static class MenuTexts
{
    public static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public const string Main = """
        1 - Registrar gasto
        2 - Registrar entrada
        3 - Resumo
        4 - Remover
        5 - Desativar bot
        """;

    public const string Paused = "Bot desativado. Para ativar, envie: ativar bot";
    public const string Resumed = "Bot ativado.";

    public const string Expense = """
        Registrar gasto
        0 - Voltar Menu Inicial
        1 - Pix
        2 - Cartao credito
        3 - Cartao debito
        4 - Retirada de caixa
        5 - Despesa Fixa
        6 - Outros
        """;

    public const string Summary = """
        Resumo
        0 - Voltar Menu Inicial
        1 - Resumo do mes
        2 - Resumo do ano
        3 - Resumo total
        4 - Filtrar data
        """;

    public const string CreditCharge = """
        Cartao credito
        0 - Voltar Menu Inicial
        1 - Numero de parcelas
        2 - A vista
        3 - Fixo
        """;

    public const string DebitCharge = """
        Cartao debito
        0 - Voltar Menu Inicial
        1 - A vista
        2 - Fixo
        """;

    public const string Remove = """
        Remover
        0 - Voltar Menu Inicial
        1 - Ultimos 10 registros
        2 - Filtrar por data
        """;

    public const string SummaryOutput = """
        Como enviar o resumo
        0 - Voltar Menu Inicial
        1 - Gerar em PDF
        2 - Gerar em texto
        """;

    public const string PdfFailed = "Nao consegui enviar o PDF.";

    public const string AskAmount = "Informe o valor";
    public const string AskInstallments = "Numero de parcelas";
    public const string AskCardName = "Informe o nome do cartao";
    public const string AskDescription = "Informe a descricao";
    public const string AskPeriod = "Informe o periodo. Ex: 10/05/2026 - 20/05/2026";
    public const string AskChargeMonth = "A partir de qual mes? Ex: 10/2026";
    public const string InvalidAmount = "Valor invalido. Ex: 32,50";
    public const string InvalidInstallments = "Numero de parcelas invalido. Ex: 10";
    public const string InvalidPeriod = "Periodo invalido. Ex: 10/05/2026 - 20/05/2026";
    public const string InvalidChargeMonth = "Mes invalido. Ex: 10/2026";
    public const string SaveFailed = "Nao consegui registrar. Tente de novo.";
    public const string InvalidCardName = "Nome do cartao invalido.";
    public const string InvalidDescription = "Descricao invalida.";
    public const string InvalidDeleteId = "Id invalido.";
    public const string NoRecords = "Nenhum registro.";
    public const string RecordNotFound = "Registro nao encontrado.";

    public static string MethodLabel(PaymentMethod method) => method switch
    {
        PaymentMethod.Pix => "Pix",
        PaymentMethod.CreditCard => "Cartao credito",
        PaymentMethod.DebitCard => "Cartao debito",
        PaymentMethod.CashWithdrawal => "Retirada de caixa",
        PaymentMethod.Fixed => "Despesa Fixa",
        PaymentMethod.Other => "Outros",
        _ => "Outros"
    };

    public static string Money(decimal amount)
    {
        var formatted = Math.Abs(amount).ToString("N2", PtBr);
        return amount < 0 ? $"-R$ {formatted}" : $"R$ {formatted}";
    }

    public static string WithMonthBalance(string reply, DateOnly month, decimal balance)
    {
        var line = $"Saldo de {month.ToString("MM/yyyy", CultureInfo.InvariantCulture)}: {Money(balance)}";
        var menu = "\n\n" + Main;
        if (reply.EndsWith(menu, StringComparison.Ordinal))
            return string.Concat(reply.AsSpan(0, reply.Length - menu.Length), "\n", line, menu);

        return reply + "\n\n" + line;
    }

    public static string CardList(IReadOnlyList<KnownCard> cards)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Escolha o cartao");
        builder.AppendLine("0 - Voltar Menu Inicial");
        for (var i = 0; i < cards.Count; i++)
            builder.AppendLine($"{i + 1} - {cards[i].Name}");
        builder.Append("99 - Registrar novo cartao");
        return builder.ToString();
    }

    public static string FormatDate(DateOnly date) =>
        date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}

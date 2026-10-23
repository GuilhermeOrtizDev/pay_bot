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
        6 - Registrar em texto
        """;

    public const string AiOption = "\n7 - Modo IA";

    public static string MainMenu(bool ai) => ai ? Main + AiOption : Main;

    public const string AiModeOn = """
        Modo IA ativado. O menu esta desligado.
        Escreva o que quer fazer. Ex: "gastei 32,50 no mercado no pix", "resumo do mes em pdf", "apagar o mercado de ontem".
        Para sair, envie: desativar IA
        """;

    public const string AiModeOff = "Modo IA desativado.";
    public const string AiHelp = "Nao entendi. Diga um gasto, uma entrada, um resumo ou o que quer remover. Para sair, envie: desativar IA";
    public const string AiFailed = "Nao consegui falar com a IA agora. Tente de novo.";
    public const string AiTranscribeOff = "Transcricao de audio desativada. Envie em texto.";
    public const string AiAudioFailed = "Nao consegui entender o audio. Repita ou envie em texto.";
    public const string AiCancelled = "Cancelado. Nada foi alterado.";
    public const string AiDeleteChoice = "Responda com o numero do registro, ou nao para cancelar.";

    public static string AiConfirm(string line) => $"Vou registrar: {line}\nResponda sim ou nao.";

    public static string AiDeleteConfirm(int index) => $"Remover o registro {index}? Responda sim ou nao.";

    public static string AiMissing(IReadOnlyList<string> fields)
    {
        var builder = new StringBuilder("Falta informar:");
        foreach (var field in fields)
            builder.Append($"\n- {AiFieldLabel(field)} (ex: {BulkExample(field)})");
        return builder.ToString();
    }

    private static string AiFieldLabel(string field) => field switch
    {
        "tipo" => "forma de pagamento (pix, cartao credito, cartao debito, retirada, despesa fixa, outros)",
        "cobranca" => "cobranca do cartao (parcelas, a vista, fixo)",
        "parcelas" => "numero de parcelas",
        "valor_tipo" => "se o valor e da parcela ou o total (parcela, total)",
        "valor" => "valor",
        "cartao" => "nome do cartao",
        "descricao" => "descricao",
        "mes" => "mes da cobranca",
        _ => field
    };

    public const string BulkPrompt = """
        Registrar em texto
        Envie o texto do lancamento.
        0 - Voltar Menu Inicial
        1 - Registro exemplo
        """;

    public const string Examples = """
        Exemplos
        0 - Voltar Menu Inicial
        1 - Pix
        2 - Cartao credito em parcelas
        3 - Cartao credito a vista
        4 - Cartao credito fixo
        5 - Cartao debito a vista
        6 - Cartao debito fixo
        7 - Retirada de caixa
        8 - Despesa fixa
        9 - Outros
        10 - Entrada
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
        1 - Tudo
        2 - Pix
        3 - Cartao credito
        4 - Cartao debito
        5 - Retirada de caixa
        6 - Despesa Fixa
        7 - Outros
        """;

    public const string SummaryPeriod = """
        Periodo do resumo
        0 - Voltar Menu Inicial
        1 - Resumo do mes
        2 - Resumo do proximo mes
        3 - Resumo do ano
        4 - Resumo total
        5 - Filtrar data
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

    public const string InstallmentAmountKindMenu = """
        O valor informado e
        0 - Voltar Menu Inicial
        1 - Valor da parcela
        2 - Valor total
        """;
    public const string AskCardName = "Informe o nome do cartao";
    public const string AskDescription = "Informe a descricao";
    public const string AskPeriod = "Informe o periodo. Ex: 10/05/2026 - 20/05/2026";
    public const string AskMonthPeriod = "Informe o mes. Ex: 01/2025 ou 01/2025 - 02/2025";
    public const string AskChargeMonth = "A partir de qual mes? Ex: 10/2026";
    public const string InvalidAmount = "Valor invalido. Ex: 32,50";
    public const string InvalidInstallments = "Numero de parcelas invalido. Ex: 10";
    public const string InvalidPeriod = "Periodo invalido. Ex: 10/05/2026 - 20/05/2026";
    public const string InvalidMonthPeriod = "Mes invalido. Ex: 01/2025 ou 01/2025 - 02/2025";
    public const string InvalidChargeMonth = "Mes invalido. Ex: 10/2026";
    public const string SaveFailed = "Nao consegui registrar. Tente de novo.";
    public const string InvalidCardName = "Nome do cartao invalido.";
    public const string InvalidDescription = "Descricao invalida.";
    public const string InvalidDeleteId = "Id invalido.";
    public const string NoRecords = "Nenhum registro.";
    public const string RecordNotFound = "Registro nao encontrado.";

    public const string Admin = """
        Administracao
        0 - Voltar Menu Inicial
        1 - Cadastrar tenant
        2 - Cadastrar grupo
        3 - Vincular grupo a tenant
        """;

    public const string AskTenantName = "Informe o nome do tenant";
    public const string AskGroupJid = "Informe o JID do grupo. Ex: 120363000000000000@g.us";
    public const string InvalidTenantName = "Nome do tenant invalido.";
    public const string InvalidGroupJid = "JID invalido. Deve terminar com @g.us";
    public const string TenantExists = "Ja existe um tenant com esse nome.";
    public const string GroupExists = "Ja existe um grupo com esse JID.";
    public const string NoGroups = "Nenhum grupo cadastrado.";
    public const string NoTenants = "Nenhum tenant cadastrado.";

    public static string GroupList(IReadOnlyList<AdminGroup> groups)
    {
        var builder = new StringBuilder("Escolha o grupo\n0 - Voltar Menu Inicial");
        for (var i = 0; i < groups.Count; i++)
            builder.Append($"\n{i + 1} - {GroupLabel(groups[i])}");
        return builder.ToString();
    }

    public static string TenantList(IReadOnlyList<AdminTenant> tenants)
    {
        var builder = new StringBuilder("Escolha o tenant\n0 - Voltar Menu Inicial");
        for (var i = 0; i < tenants.Count; i++)
            builder.Append($"\n{i + 1} - {tenants[i].Name}");
        return builder.ToString();
    }

    public static string GroupLabel(AdminGroup group) =>
        $"{group.Jid} ({(string.IsNullOrWhiteSpace(group.TenantName) ? "sem tenant" : "tenant " + group.TenantName)})";

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
        foreach (var menu in new[] { "\n\n" + MainMenu(true), "\n\n" + Main })
        {
            if (reply.EndsWith(menu, StringComparison.Ordinal))
                return string.Concat(reply.AsSpan(0, reply.Length - menu.Length), "\n", line, menu);
        }

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

    public static string SummaryCardList(IReadOnlyList<KnownCard> cards)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Escolha o cartao");
        builder.AppendLine("0 - Voltar Menu Inicial");
        builder.Append("1 - Todos os cartoes");
        for (var i = 0; i < cards.Count; i++)
            builder.Append($"\n{i + 2} - {cards[i].Name}");
        return builder.ToString();
    }

    public static string FormatDate(DateOnly date) =>
        date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string? Example(string option) => option switch
    {
        "1" => """
            gasto
            tipo: pix
            valor: 32,50
            descricao: mercado
            """,
        "2" => """
            gasto
            tipo: cartao credito
            cobranca: parcelas
            parcelas: 10
            valor_tipo: total
            valor: 1200,00
            cartao: Nubank
            descricao: notebook
            mes: 10/2026
            """,
        "3" => """
            gasto
            tipo: cartao credito
            cobranca: a vista
            valor: 89,90
            cartao: Nubank
            descricao: farmacia
            mes: 10/2026
            """,
        "4" => """
            gasto
            tipo: cartao credito
            cobranca: fixo
            valor: 49,90
            cartao: Nubank
            descricao: netflix
            mes: 10/2026
            """,
        "5" => """
            gasto
            tipo: cartao debito
            cobranca: a vista
            valor: 45,00
            cartao: Inter
            descricao: almoco
            """,
        "6" => """
            gasto
            tipo: cartao debito
            cobranca: fixo
            valor: 19,90
            cartao: Inter
            descricao: spotify
            """,
        "7" => """
            gasto
            tipo: retirada
            valor: 200,00
            descricao: caixa
            """,
        "8" => """
            gasto
            tipo: despesa fixa
            valor: 1800,00
            descricao: aluguel
            mes: 10/2026
            """,
        "9" => """
            gasto
            tipo: outros
            valor: 15,00
            descricao: gorjeta
            mes: 10/2026
            """,
        "10" => """
            entrada
            valor: 5000,00
            descricao: salario
            mes: 10/2026
            """,
        _ => null
    };

    public static string BulkProblems(IReadOnlyList<string> fields)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Faltou ou esta invalido:");
        foreach (var field in fields)
            builder.AppendLine($"- {field} (ex: {BulkExample(field)})");
        builder.AppendLine();
        builder.AppendLine(fields.Count == 1
            ? "Reenvie o texto completo, ou so a linha:"
            : "Reenvie o texto completo, ou as linhas:");
        for (var i = 0; i < fields.Count; i++)
        {
            var line = $"{fields[i]}: {BulkExample(fields[i])}";
            if (i == fields.Count - 1)
                builder.Append(line);
            else
                builder.AppendLine(line);
        }

        return builder.ToString();
    }

    private static string BulkExample(string field) => field switch
    {
        "tipo" => "pix",
        "cobranca" => "parcelas",
        "parcelas" => "10",
        "valor_tipo" => "total",
        "valor" => "32,50",
        "cartao" => "Nubank",
        "descricao" => "mercado",
        "mes" => "10/2026",
        _ => ""
    };
}

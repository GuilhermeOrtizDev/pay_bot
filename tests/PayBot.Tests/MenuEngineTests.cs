using PayBot.Domain;
using PayBot.Menu;

namespace PayBot.Tests;

public class MenuEngineTests
{
    private static readonly DateOnly Today = new(2026, 10, 13);
    private static readonly Guid NubankId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid InterId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Main_menu_opens_expense_income_and_summary()
    {
        var expense = Act("1", MenuStep.Main);
        Assert.Equal(MenuStep.ExpenseMenu, expense.NextStep);
        Assert.Contains("6 - Outros", expense.Reply);

        var income = Act("2", MenuStep.Main);
        Assert.Equal(MenuStep.AwaitingIncomeAmount, income.NextStep);

        var summary = Act("3", MenuStep.Main);
        Assert.Equal(MenuStep.SummaryMenu, summary.NextStep);
        Assert.Contains("2 - Resumo do proximo mes", summary.Reply);
        Assert.Contains("5 - Filtrar data", summary.Reply);
        Assert.DoesNotContain("5 - Remover", summary.Reply);

        var remove = Act("4", MenuStep.Main);
        Assert.Equal(MenuStep.RemoveMenu, remove.NextStep);
        Assert.Contains("1 - Ultimos 10 registros", remove.Reply);

        var paused = Act("5", MenuStep.Main);
        Assert.Equal(MenuStep.Paused, paused.NextStep);
        Assert.Contains("ativar bot", paused.Reply);
    }

    [Fact]
    public void Paused_bot_answers_only_to_ativar_bot()
    {
        var ignored = Act("1", MenuStep.Paused);
        Assert.Equal(MenuStep.Paused, ignored.NextStep);
        Assert.Equal("", ignored.Reply);

        var zero = Act("0", MenuStep.Paused, new SessionDraft { Amount = 10 });
        Assert.Equal(MenuStep.Paused, zero.NextStep);
        Assert.Equal("", zero.Reply);

        var resumed = Act("Ativar Bot", MenuStep.Paused);
        Assert.Equal(MenuStep.Main, resumed.NextStep);
        Assert.Contains("Bot ativado.", resumed.Reply);
        Assert.Contains("5 - Desativar bot", resumed.Reply);
    }

    [Fact]
    public void Zero_returns_to_main_and_clears_draft()
    {
        var draft = new SessionDraft { Method = PaymentMethod.Pix, Amount = 10 };
        var turn = Act("0", MenuStep.AwaitingAmount, draft);

        Assert.Equal(MenuStep.Main, turn.NextStep);
        Assert.Null(turn.Draft.Method);
        Assert.Null(turn.Draft.Amount);
        Assert.Null(turn.Expense);
    }

    [Fact]
    public void Invalid_expense_option_repeats_the_menu()
    {
        var turn = Act("9", MenuStep.ExpenseMenu);
        Assert.Equal(MenuStep.ExpenseMenu, turn.NextStep);
        Assert.Contains("Registrar gasto", turn.Reply);
        Assert.Null(turn.Expense);
    }

    [Theory]
    [InlineData("1", PaymentMethod.Pix, "Pix")]
    [InlineData("4", PaymentMethod.CashWithdrawal, "Retirada de caixa")]
    public void Simple_expense_saves_the_amount(string option, PaymentMethod method, string label)
    {
        var chosen = Act(option, MenuStep.ExpenseMenu);
        var amount = Act("32,50", chosen.NextStep, chosen.Draft);
        Assert.Equal(MenuStep.AwaitingExpenseDescription, amount.NextStep);

        var saved = Act("padaria", amount.NextStep, amount.Draft);

        Assert.Equal(MenuStep.Main, saved.NextStep);
        Assert.NotNull(saved.Expense);
        Assert.Equal(method, saved.Expense.Method);
        Assert.Equal(32.50m, saved.Expense.Amount);
        Assert.Equal("padaria", saved.Expense.Description);
        Assert.Null(saved.Expense.CardId);
        Assert.Null(saved.Expense.Installments);
        Assert.False(saved.Expense.IsFixed);
        Assert.Contains(label, saved.Reply);
        Assert.Contains("R$ 32,50", saved.Reply);
        Assert.Contains("padaria", saved.Reply);
        Assert.Contains("1 - Registrar gasto", saved.Reply);
    }

    [Fact]
    public void Credit_card_keeps_installments_and_existing_card()
    {
        var chosen = Act("2", MenuStep.ExpenseMenu);
        var parcels = Act("1", chosen.NextStep, chosen.Draft);
        var count = Act("10", parcels.NextStep, parcels.Draft);
        var amount = Act("1.200,00", count.NextStep, count.Draft, Cards());
        Assert.Equal(MenuStep.AwaitingCardChoice, amount.NextStep);
        Assert.Contains("Nubank", amount.Reply);
        Assert.DoesNotContain("Inter", amount.Reply);

        var card = Act("1", amount.NextStep, amount.Draft, Cards());
        var described = Act("mercado", card.NextStep, card.Draft, Cards());
        Assert.Equal(MenuStep.AwaitingChargeMonth, described.NextStep);

        var saved = Act("11/2026", described.NextStep, described.Draft, Cards());
        Assert.Equal(NubankId, saved.Expense!.CardId);
        Assert.Equal(10, saved.Expense.Installments);
        Assert.Equal("mercado", saved.Expense.Description);
        Assert.Equal(new DateOnly(2026, 11, 1), saved.Expense.ChargeMonth);
        Assert.False(saved.Expense.IsFixed);
        Assert.False(saved.Expense.CreateCard);
        Assert.Equal(1200m, saved.Expense.Amount);
        Assert.Contains("em 10x", saved.Reply);
        Assert.Contains("a partir de 11/2026", saved.Reply);
        Assert.Contains("Nubank", saved.Reply);
        Assert.Contains("mercado", saved.Reply);
    }

    [Theory]
    [InlineData("5", PaymentMethod.Fixed, "Despesa Fixa")]
    [InlineData("6", PaymentMethod.Other, "Outros")]
    public void Fixed_and_other_expenses_start_on_the_chosen_month(string option, PaymentMethod method, string label)
    {
        var chosen = Act(option, MenuStep.ExpenseMenu);
        var amount = Act("80", chosen.NextStep, chosen.Draft);
        var described = Act("aluguel", amount.NextStep, amount.Draft);
        Assert.Equal(MenuStep.AwaitingChargeMonth, described.NextStep);

        var invalid = Act("2026", described.NextStep, described.Draft);
        Assert.Equal(MenuStep.AwaitingChargeMonth, invalid.NextStep);
        Assert.Null(invalid.Expense);

        var saved = Act("01/2027", invalid.NextStep, invalid.Draft);
        Assert.Equal(method, saved.Expense!.Method);
        Assert.Equal(new DateOnly(2027, 1, 1), saved.Expense.ChargeMonth);
        Assert.Equal("aluguel", saved.Expense.Description);
        Assert.Contains(label, saved.Reply);
        Assert.Contains("a partir de 01/2027", saved.Reply);
    }

    [Fact]
    public void Credit_card_in_full_and_fixed_skip_installments()
    {
        var credit = Act("2", MenuStep.ExpenseMenu);
        var inFull = Act("2", credit.NextStep, credit.Draft);
        Assert.Equal(MenuStep.AwaitingAmount, inFull.NextStep);
        Assert.Equal(CardBilling.InFull, inFull.Draft.Billing);

        var fixedChoice = Act("3", MenuStep.AwaitingCreditCharge, new SessionDraft { Method = PaymentMethod.CreditCard });
        Assert.Equal(MenuStep.AwaitingAmount, fixedChoice.NextStep);
        Assert.Equal(CardBilling.Fixed, fixedChoice.Draft.Billing);
    }

    [Fact]
    public void Debit_card_offers_in_full_or_fixed()
    {
        var debit = Act("3", MenuStep.ExpenseMenu);
        Assert.Equal(MenuStep.AwaitingDebitCharge, debit.NextStep);
        Assert.Contains("2 - Fixo", debit.Reply);
        Assert.DoesNotContain("Numero de parcelas", debit.Reply);

        var fixedChoice = Act("2", debit.NextStep, debit.Draft);
        var amount = Act("40,00", fixedChoice.NextStep, fixedChoice.Draft, Cards());
        var card = Act("1", amount.NextStep, amount.Draft, Cards());
        var saved = Act("netflix", card.NextStep, card.Draft, Cards());

        Assert.True(saved.Expense!.IsFixed);
        Assert.Equal(InterId, saved.Expense.CardId);
        Assert.Equal("netflix", saved.Expense.Description);
        Assert.Contains("fixo", saved.Reply);
    }

    [Fact]
    public void Zero_at_installments_goes_back_instead_of_saving()
    {
        var draft = new SessionDraft { Method = PaymentMethod.CreditCard, Amount = 100 };
        var turn = Act("0", MenuStep.AwaitingInstallments, draft);
        Assert.Equal(MenuStep.Main, turn.NextStep);
        Assert.Null(turn.Expense);
    }

    [Fact]
    public void Option_99_asks_for_a_new_card_and_saves_it()
    {
        var draft = new SessionDraft { Method = PaymentMethod.DebitCard, Amount = 40, Billing = CardBilling.InFull };
        var ask = Act("99", MenuStep.AwaitingCardChoice, draft, Cards());
        Assert.Equal(MenuStep.AwaitingNewCardName, ask.NextStep);

        var named = Act("C6", ask.NextStep, ask.Draft, Cards());
        Assert.Equal(MenuStep.AwaitingExpenseDescription, named.NextStep);

        var saved = Act("farmacia", named.NextStep, named.Draft, Cards());
        Assert.True(saved.Expense!.CreateCard);
        Assert.Equal("C6", saved.Expense.CardName);
        Assert.Equal("farmacia", saved.Expense.Description);
        Assert.Null(saved.Expense.CardId);
        Assert.Equal(PaymentMethod.DebitCard, saved.Expense.Method);
        Assert.Equal(40m, saved.Expense.Amount);
        Assert.Contains("a vista", saved.Reply);
    }

    [Fact]
    public void Repeated_card_name_reuses_the_existing_card()
    {
        var draft = new SessionDraft { Method = PaymentMethod.DebitCard, Amount = 15, Billing = CardBilling.InFull };
        var named = Act("inter", MenuStep.AwaitingNewCardName, draft, Cards());
        var saved = Act("almoco", named.NextStep, named.Draft, Cards());

        Assert.False(saved.Expense!.CreateCard);
        Assert.Equal(InterId, saved.Expense.CardId);
        Assert.Equal("Inter", saved.Expense.CardName);
        Assert.Equal("almoco", saved.Expense.Description);
    }

    [Fact]
    public void Debit_list_hides_credit_cards()
    {
        var draft = new SessionDraft { Method = PaymentMethod.DebitCard, Amount = 10 };
        var turn = Act("40,00", MenuStep.AwaitingAmount, draft, Cards());
        Assert.Contains("Inter", turn.Reply);
        Assert.DoesNotContain("Nubank", turn.Reply);
    }

    [Fact]
    public void Income_asks_description_then_month()
    {
        var amount = Act("3000", MenuStep.AwaitingIncomeAmount);
        var described = Act("salario", amount.NextStep, amount.Draft);
        Assert.Equal(MenuStep.AwaitingIncomeMonth, described.NextStep);

        var invalid = Act("salario", described.NextStep, described.Draft);
        Assert.Equal(MenuStep.AwaitingIncomeMonth, invalid.NextStep);
        Assert.Null(invalid.Income);

        var saved = Act("10/2026", invalid.NextStep, invalid.Draft);
        Assert.Equal(3000m, saved.Income!.Amount);
        Assert.Equal("salario", saved.Income.Description);
        Assert.Equal(new DateOnly(2026, 10, 1), saved.Income.ChargeMonth);
        Assert.Contains("Entrada registrada: R$ 3.000,00 a partir de 10/2026 - salario", saved.Reply);
        Assert.Equal(MenuStep.Main, saved.NextStep);
    }

    [Fact]
    public void Confirmation_places_the_month_balance_before_the_menu()
    {
        var reply = "Gasto registrado: Pix R$ 10,00 - pao\n\n" + MenuTexts.Main;
        var withBalance = MenuTexts.WithMonthBalance(reply, new DateOnly(2026, 10, 1), -15.5m);
        var text = withBalance.Replace("\r\n", "\n");

        Assert.Contains("Saldo de 10/2026: -R$ 15,50\n\n1 - Registrar gasto", text);
    }

    [Fact]
    public void Summary_options_cover_month_year_total_and_custom_range()
    {
        var month = Act("1", MenuStep.SummaryMenu);
        Assert.Equal(MenuStep.AwaitingSummaryOutput, month.NextStep);
        Assert.Contains("1 - Gerar em PDF", month.Reply);
        Assert.Contains("2 - Gerar em texto", month.Reply);
        Assert.Equal(new DateOnly(2026, 10, 1), month.Draft.SummaryFrom);
        Assert.Equal(new DateOnly(2026, 10, 31), month.Draft.SummaryTo);

        var asText = Act("2", month.NextStep, month.Draft);
        Assert.False(asText.AsPdf);
        Assert.Equal(new DateOnly(2026, 10, 1), asText.Summary!.From);
        Assert.Equal(new DateOnly(2026, 10, 31), asText.Summary.To);
        Assert.Equal(MenuStep.SummaryMenu, asText.NextStep);

        var asPdf = Act("1", month.NextStep, month.Draft);
        Assert.True(asPdf.AsPdf);
        Assert.Equal(month.Draft.SummaryTitle, asPdf.Summary!.Title);

        var nextMonth = Act("2", MenuStep.SummaryMenu);
        Assert.Equal(new DateOnly(2026, 11, 1), nextMonth.Draft.SummaryFrom);
        Assert.Equal(new DateOnly(2026, 11, 30), nextMonth.Draft.SummaryTo);

        var year = Act("3", MenuStep.SummaryMenu);
        Assert.Equal(new DateOnly(2026, 1, 1), year.Draft.SummaryFrom);
        Assert.Equal(new DateOnly(2026, 12, 31), year.Draft.SummaryTo);

        var total = Act("4", MenuStep.SummaryMenu);
        Assert.Null(total.Draft.SummaryFrom);
        Assert.Null(total.Draft.SummaryTo);
        Assert.Equal("Resumo total", total.Draft.SummaryTitle);

        var ask = Act("5", MenuStep.SummaryMenu);
        Assert.Equal(MenuStep.AwaitingDateRange, ask.NextStep);

        var single = Act("01/2025", ask.NextStep);
        Assert.Equal(MenuStep.AwaitingSummaryOutput, single.NextStep);
        Assert.Equal(new DateOnly(2025, 1, 1), single.Draft.SummaryFrom);
        Assert.Equal(new DateOnly(2025, 1, 31), single.Draft.SummaryTo);
        Assert.Equal("Resumo de 01/2025", single.Draft.SummaryTitle);

        var range = Act("01/2025 - 02/2025", ask.NextStep);
        Assert.Equal(MenuStep.AwaitingSummaryOutput, range.NextStep);
        Assert.Equal(new DateOnly(2025, 1, 1), range.Draft.SummaryFrom);
        Assert.Equal(new DateOnly(2025, 2, 28), range.Draft.SummaryTo);
        Assert.Equal("Resumo de 01/2025 a 02/2025", range.Draft.SummaryTitle);

        var invalid = Act("ontem", MenuStep.AwaitingDateRange);
        Assert.Equal(MenuStep.AwaitingDateRange, invalid.NextStep);
        Assert.Null(invalid.Summary);
        Assert.Contains("Mes invalido", invalid.Reply);

    }

    [Fact]
    public void Remove_lists_by_recency_or_date_and_deletes_the_selected_id()
    {
        var latest = Act("1", MenuStep.RemoveMenu);
        Assert.Equal(MenuStep.AwaitingDeleteChoice, latest.NextStep);
        Assert.Equal(10, latest.DeleteList!.Take);
        Assert.Null(latest.DeleteList.From);

        var ask = Act("2", MenuStep.RemoveMenu);
        var ranged = Act("01/10/2026 - 13/10/2026", ask.NextStep);
        Assert.Equal(new DateOnly(2026, 10, 1), ranged.DeleteList!.From);
        Assert.Equal(new DateOnly(2026, 10, 13), ranged.DeleteList.To);

        var first = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var second = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var draft = new SessionDraft { DeleteIds = [first, second] };
        var deleted = Act("2", MenuStep.AwaitingDeleteChoice, draft);
        Assert.Equal(second, deleted.DeleteId);
        Assert.Equal(MenuStep.RemoveMenu, deleted.NextStep);

        var invalid = Act("9", MenuStep.AwaitingDeleteChoice, draft);
        Assert.Null(invalid.DeleteId);
        Assert.Equal(MenuStep.AwaitingDeleteChoice, invalid.NextStep);
        Assert.Contains("Id invalido", invalid.Reply);
    }

    [Fact]
    public void Text_entry_waits_for_the_block_and_examples_are_selectable()
    {
        var entry = Act("6", MenuStep.Main);
        Assert.Equal(MenuStep.AwaitingBulkEntry, entry.NextStep);
        Assert.Contains("Envie o texto do lancamento.", entry.Reply);
        Assert.Contains("1 - Registro exemplo", entry.Reply);
        Assert.Null(entry.Expense);

        var repeated = Act("oi", entry.NextStep);
        Assert.Equal(MenuStep.AwaitingBulkEntry, repeated.NextStep);
        Assert.Null(repeated.Expense);

        var saved = Act("""
            gasto
            tipo: pix
            valor: 32,50
            descricao: mercado
            """, entry.NextStep);
        Assert.Equal(MenuStep.Main, saved.NextStep);
        Assert.Equal(32.50m, saved.Expense!.Amount);
        Assert.Equal("mercado", saved.Expense.Description);

        var examples = Act("1", entry.NextStep);
        Assert.Equal(MenuStep.ExampleMenu, examples.NextStep);
        Assert.Contains("1 - Pix", examples.Reply);
        Assert.Contains("2 - Cartao credito em parcelas", examples.Reply);
        Assert.Contains("6 - Cartao debito fixo", examples.Reply);
        Assert.Contains("10 - Entrada", examples.Reply);

        var invalid = Act("11", examples.NextStep);
        Assert.Equal(MenuStep.ExampleMenu, invalid.NextStep);
        Assert.Contains("10 - Entrada", invalid.Reply);

        var pix = Act("1", examples.NextStep);
        Assert.Equal(MenuStep.ExampleMenu, pix.NextStep);
        Assert.Contains("tipo: pix", pix.Reply);
        Assert.Contains("valor: 32,50", pix.Reply);
        Assert.Contains("descricao: mercado", pix.Reply);
        Assert.Null(pix.Expense);

        var income = Act("10", pix.NextStep);
        Assert.Contains("entrada", income.Reply);
        Assert.Contains("descricao: salario", income.Reply);
        Assert.Contains("mes: 10/2026", income.Reply);
        Assert.Equal(MenuStep.ExampleMenu, income.NextStep);

        var back = Act("0", income.NextStep);
        Assert.Equal(MenuStep.Main, back.NextStep);
        Assert.Contains("6 - Registrar em texto", back.Reply);
        Assert.DoesNotContain("Exemplos", back.Reply);
        Assert.DoesNotContain("Registro exemplo", back.Reply);
    }

    [Theory]
    [InlineData("2", "cobranca: parcelas")]
    [InlineData("3", "cobranca: a vista")]
    [InlineData("4", "cobranca: fixo")]
    [InlineData("5", "tipo: cartao debito")]
    [InlineData("6", "descricao: spotify")]
    [InlineData("7", "tipo: retirada")]
    [InlineData("8", "tipo: despesa fixa")]
    [InlineData("9", "tipo: outros")]
    public void Each_example_returns_a_filled_sample(string option, string snippet)
    {
        var turn = Act(option, MenuStep.ExampleMenu);
        Assert.Equal(MenuStep.ExampleMenu, turn.NextStep);
        Assert.Contains(snippet, turn.Reply);
        Assert.Contains("1 - Pix", turn.Reply);
        Assert.Null(turn.Expense);
        Assert.Null(turn.Income);
    }

    [Fact]
    public void Bulk_pix_saves_in_one_message()
    {
        var saved = Act("""
            gasto
            descricao: mercado
            valor: 32,50
            tipo: pix
            """, MenuStep.Main);

        Assert.Equal(MenuStep.Main, saved.NextStep);
        Assert.Equal(PaymentMethod.Pix, saved.Expense!.Method);
        Assert.Equal(32.50m, saved.Expense.Amount);
        Assert.Equal("mercado", saved.Expense.Description);
        Assert.Null(saved.Expense.ChargeMonth);
        Assert.Null(saved.Expense.CardId);
        Assert.False(saved.Expense.IsFixed);
        Assert.Contains("Pix", saved.Reply);
        Assert.Contains("R$ 32,50", saved.Reply);
    }

    [Fact]
    public void Bulk_credit_installments_reuse_an_existing_card()
    {
        var saved = Act("""
            gasto
            tipo: cartão crédito
            cobranca: parcelas
            parcelas: 10
            valor: 1.200,00
            cartao: nubank
            descricao: notebook
            mes: 11/2026
            """, MenuStep.Main, cards: Cards());

        Assert.Equal(NubankId, saved.Expense!.CardId);
        Assert.Equal("Nubank", saved.Expense.CardName);
        Assert.False(saved.Expense.CreateCard);
        Assert.Equal(10, saved.Expense.Installments);
        Assert.Equal(1200m, saved.Expense.Amount);
        Assert.Equal(new DateOnly(2026, 11, 1), saved.Expense.ChargeMonth);
        Assert.False(saved.Expense.IsFixed);
        Assert.Contains("em 10x", saved.Reply);
        Assert.Contains("a partir de 11/2026", saved.Reply);
    }

    [Fact]
    public void Bulk_credit_accepts_accents_and_creates_a_new_card()
    {
        var saved = Act("""
            gasto
            tipo: cartão de crédito
            cobrança: à vista
            valor: 89,90
            cartão: C6
            descrição: farmácia
            mês: 10/2026
            """, MenuStep.Main, cards: Cards());

        Assert.True(saved.Expense!.CreateCard);
        Assert.Null(saved.Expense.CardId);
        Assert.Equal("C6", saved.Expense.CardName);
        Assert.Equal("farmácia", saved.Expense.Description);
        Assert.Null(saved.Expense.Installments);
        Assert.False(saved.Expense.IsFixed);
        Assert.Equal(new DateOnly(2026, 10, 1), saved.Expense.ChargeMonth);
        Assert.Contains("a vista", saved.Reply);
    }

    [Theory]
    [InlineData("a vista", false)]
    [InlineData("fixo", true)]
    public void Bulk_debit_skips_the_month(string billing, bool isFixed)
    {
        var saved = Act($"""
            gasto
            tipo: cartao debito
            cobranca: {billing}
            valor: 45,00
            cartao: Inter
            descricao: almoco
            mes: 10/2026
            """, MenuStep.Main, cards: Cards());

        Assert.Equal(InterId, saved.Expense!.CardId);
        Assert.Equal(isFixed, saved.Expense.IsFixed);
        Assert.Null(saved.Expense.ChargeMonth);
        Assert.DoesNotContain("a partir de", saved.Reply);
    }

    [Fact]
    public void Bulk_fixed_expense_and_income_keep_the_month()
    {
        var expense = Act("""
            gasto
            tipo: despesa fixa
            valor: 1800
            descricao: aluguel
            mes: 01/2027
            """, MenuStep.Main);
        Assert.Equal(PaymentMethod.Fixed, expense.Expense!.Method);
        Assert.Equal(new DateOnly(2027, 1, 1), expense.Expense.ChargeMonth);
        Assert.Contains("a partir de 01/2027", expense.Reply);

        var income = Act("""
            entrada
            valor: 5.000,00
            descricao: salario
            mes: 10/2026
            """, MenuStep.Main);
        Assert.Null(income.Expense);
        Assert.Equal(5000m, income.Income!.Amount);
        Assert.Equal("salario", income.Income.Description);
        Assert.Equal(new DateOnly(2026, 10, 1), income.Income.ChargeMonth);
    }

    [Fact]
    public void Bulk_reports_missing_fields_and_accepts_one_correction_line()
    {
        var missing = Act("""
            gasto
            tipo: cartao credito
            cobranca: parcelas
            parcelas: 10
            valor: 1200
            cartao: Nubank
            descricao: notebook
            """, MenuStep.Main, cards: Cards());

        Assert.Equal(MenuStep.AwaitingBulkFix, missing.NextStep);
        Assert.Null(missing.Expense);
        Assert.Contains("Faltou ou esta invalido:", missing.Reply);
        Assert.Contains("- mes (ex: 10/2026)", missing.Reply);
        Assert.Contains("ou so a linha:", missing.Reply);
        Assert.DoesNotContain("- tipo", missing.Reply);

        var ignored = Act("oi", missing.NextStep, missing.Draft, Cards());
        Assert.Equal(MenuStep.AwaitingBulkFix, ignored.NextStep);
        Assert.Null(ignored.Expense);
        Assert.Equal(1200m, ignored.Draft.Amount);

        var saved = Act("mes: 11/2026", ignored.NextStep, ignored.Draft, Cards());
        Assert.Equal(MenuStep.Main, saved.NextStep);
        Assert.Equal(NubankId, saved.Expense!.CardId);
        Assert.Equal(new DateOnly(2026, 11, 1), saved.Expense.ChargeMonth);
    }

    [Fact]
    public void Bulk_invalid_values_can_be_replaced_by_a_new_block()
    {
        var invalid = Act("""
            gasto
            tipo: cartao debito
            cobranca: parcelas
            valor: 0
            descricao: almoco
            """, MenuStep.Main, cards: Cards());

        Assert.Equal(MenuStep.AwaitingBulkFix, invalid.NextStep);
        Assert.Contains("- cobranca", invalid.Reply);
        Assert.Contains("- valor", invalid.Reply);
        Assert.Contains("- cartao", invalid.Reply);
        Assert.Null(invalid.Expense);

        var replaced = Act("""
            entrada
            valor: 80
            descricao: bonus
            mes: 02/2027
            """, invalid.NextStep, invalid.Draft);

        Assert.Null(replaced.Expense);
        Assert.Equal(80m, replaced.Income!.Amount);
        Assert.Equal("bonus", replaced.Income.Description);
    }

    [Fact]
    public void Bulk_fix_zero_discards_the_draft()
    {
        var missing = Act("entrada\nvalor: 10", MenuStep.Main);
        var back = Act("0", missing.NextStep, missing.Draft);

        Assert.Equal(MenuStep.Main, back.NextStep);
        Assert.Null(back.Draft.Amount);
        Assert.Null(back.Income);
    }

    [Fact]
    public void Bulk_text_does_not_interrupt_the_step_menu_or_a_paused_bot()
    {
        var block = """
            gasto
            tipo: pix
            valor: 10
            descricao: pao
            """;

        var menu = Act(block, MenuStep.ExpenseMenu);
        Assert.Equal(MenuStep.ExpenseMenu, menu.NextStep);
        Assert.Null(menu.Expense);

        var paused = Act(block, MenuStep.Paused);
        Assert.Equal(MenuStep.Paused, paused.NextStep);
        Assert.Equal("", paused.Reply);
    }

    [Fact]
    public void Bulk_message_uses_only_the_first_block()
    {
        var saved = Act("""
            gasto
            tipo: pix
            valor: 10
            descricao: pao

            entrada
            valor: 99
            descricao: salario
            mes: 10/2026
            """, MenuStep.Main);

        Assert.NotNull(saved.Expense);
        Assert.Null(saved.Income);
        Assert.Equal(10m, saved.Expense.Amount);
        Assert.Equal("pao", saved.Expense.Description);
        Assert.Null(saved.Expense.ChargeMonth);
    }

    [Fact]
    public void Ai_option_is_hidden_and_ignored_when_ai_is_off()
    {
        var menu = Act("x", MenuStep.Main);
        Assert.DoesNotContain("7 - Modo IA", menu.Reply);

        var seven = Act("7", MenuStep.Main);
        Assert.Equal(MenuStep.Main, seven.NextStep);
        Assert.DoesNotContain("7 - Modo IA", seven.Reply);
    }

    [Fact]
    public void Ai_option_shows_in_every_main_menu_and_enters_ai_mode()
    {
        var menu = Act("x", MenuStep.Main, ai: true);
        Assert.EndsWith("6 - Registrar em texto" + MenuTexts.AiOption, menu.Reply);

        var saved = Act("pao", MenuStep.AwaitingExpenseDescription, new SessionDraft { Method = PaymentMethod.Pix, Amount = 10m }, ai: true);
        Assert.NotNull(saved.Expense);
        Assert.EndsWith(MenuTexts.AiOption, saved.Reply);

        var ai = Act("7", MenuStep.Main, ai: true);
        Assert.Equal(MenuStep.AiMode, ai.NextStep);
        Assert.Contains("desativar IA", ai.Reply);
    }

    [Fact]
    public void Ai_step_returns_to_main_menu_when_ai_is_off()
    {
        var turn = Act("gastei 10 no pix", MenuStep.AiMode);
        Assert.Equal(MenuStep.Main, turn.NextStep);
        Assert.Equal(MenuTexts.Main, turn.Reply);
    }

    [Fact]
    public void Month_balance_goes_before_the_menu_with_ai_option()
    {
        var reply = "Gasto registrado: Pix R$ 10,00 - pao\n\n" + MenuTexts.MainMenu(true);
        var text = MenuTexts.WithMonthBalance(reply, new DateOnly(2026, 10, 1), 5m).Replace("\r\n", "\n");
        Assert.Contains("Saldo de 10/2026: R$ 5,00\n\n1 - Registrar gasto", text);
        Assert.EndsWith("7 - Modo IA", text);
    }

    private static MenuTurn Act(
        string text,
        MenuStep step,
        SessionDraft? draft = null,
        IReadOnlyList<KnownCard>? cards = null,
        bool ai = false) =>
        MenuEngine.Handle(text, step, draft ?? new SessionDraft(), cards ?? [], Today, ai);

    private static IReadOnlyList<KnownCard> Cards() =>
    [
        new KnownCard(NubankId, "Nubank", CardKind.Credit),
        new KnownCard(InterId, "Inter", CardKind.Debit)
    ];
}

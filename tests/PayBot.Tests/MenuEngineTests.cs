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
        Assert.Contains("4 - Filtrar data", summary.Reply);
        Assert.DoesNotContain("5 - Remover", summary.Reply);

        var remove = Act("4", MenuStep.Main);
        Assert.Equal(MenuStep.RemoveMenu, remove.NextStep);
        Assert.Contains("1 - Ultimos 10 registros", remove.Reply);
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
    public void Income_asks_description_then_saves()
    {
        var amount = Act("3000", MenuStep.AwaitingIncomeAmount);
        var saved = Act("salario", amount.NextStep, amount.Draft);

        Assert.Equal(3000m, saved.Income!.Amount);
        Assert.Equal("salario", saved.Income.Description);
        Assert.Contains("Entrada registrada: R$ 3.000,00 - salario", saved.Reply);
        Assert.Equal(MenuStep.Main, saved.NextStep);
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

        var year = Act("2", MenuStep.SummaryMenu);
        Assert.Equal(new DateOnly(2026, 1, 1), year.Draft.SummaryFrom);
        Assert.Equal(new DateOnly(2026, 12, 31), year.Draft.SummaryTo);

        var total = Act("3", MenuStep.SummaryMenu);
        Assert.Null(total.Draft.SummaryFrom);
        Assert.Null(total.Draft.SummaryTo);
        Assert.Equal("Resumo total", total.Draft.SummaryTitle);

        var ask = Act("4", MenuStep.SummaryMenu);
        Assert.Equal(MenuStep.AwaitingDateRange, ask.NextStep);

        var range = Act("10/05/2026 - 20/05/2026", ask.NextStep);
        Assert.Equal(MenuStep.AwaitingSummaryOutput, range.NextStep);
        Assert.Equal(new DateOnly(2026, 5, 10), range.Draft.SummaryFrom);
        Assert.Equal(new DateOnly(2026, 5, 20), range.Draft.SummaryTo);

        var invalid = Act("ontem", MenuStep.AwaitingDateRange);
        Assert.Equal(MenuStep.AwaitingDateRange, invalid.NextStep);
        Assert.Null(invalid.Summary);
        Assert.Contains("Periodo invalido", invalid.Reply);

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

    private static MenuTurn Act(
        string text,
        MenuStep step,
        SessionDraft? draft = null,
        IReadOnlyList<KnownCard>? cards = null) =>
        MenuEngine.Handle(text, step, draft ?? new SessionDraft(), cards ?? [], Today);

    private static IReadOnlyList<KnownCard> Cards() =>
    [
        new KnownCard(NubankId, "Nubank", CardKind.Credit),
        new KnownCard(InterId, "Inter", CardKind.Debit)
    ];
}

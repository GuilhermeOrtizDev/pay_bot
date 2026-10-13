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
    [InlineData("5", PaymentMethod.Fixed, "Despesa Fixa")]
    [InlineData("6", PaymentMethod.Other, "Outros")]
    public void Simple_expense_saves_the_amount(string option, PaymentMethod method, string label)
    {
        var chosen = Act(option, MenuStep.ExpenseMenu);
        var saved = Act("32,50", chosen.NextStep, chosen.Draft);

        Assert.Equal(MenuStep.Main, saved.NextStep);
        Assert.NotNull(saved.Expense);
        Assert.Equal(method, saved.Expense.Method);
        Assert.Equal(32.50m, saved.Expense.Amount);
        Assert.Null(saved.Expense.CardId);
        Assert.Null(saved.Expense.Installments);
        Assert.Contains(label, saved.Reply);
        Assert.Contains("R$ 32,50", saved.Reply);
        Assert.Contains("1 - Registrar gasto", saved.Reply);
    }

    [Fact]
    public void Credit_card_keeps_installments_and_existing_card()
    {
        var amount = Act("1.200,00", MenuStep.AwaitingAmount, new SessionDraft { Method = PaymentMethod.CreditCard });
        var parcels = Act("10", amount.NextStep, amount.Draft, Cards());
        Assert.Equal(MenuStep.AwaitingCardChoice, parcels.NextStep);
        Assert.Contains("Nubank", parcels.Reply);
        Assert.DoesNotContain("Inter", parcels.Reply);

        var saved = Act("1", parcels.NextStep, parcels.Draft, Cards());
        Assert.Equal(NubankId, saved.Expense!.CardId);
        Assert.Equal(10, saved.Expense.Installments);
        Assert.False(saved.Expense.CreateCard);
        Assert.Equal(1200m, saved.Expense.Amount);
        Assert.Contains("em 10x", saved.Reply);
        Assert.Contains("Nubank", saved.Reply);
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
        var draft = new SessionDraft { Method = PaymentMethod.DebitCard, Amount = 40 };
        var ask = Act("99", MenuStep.AwaitingCardChoice, draft, Cards());
        Assert.Equal(MenuStep.AwaitingNewCardName, ask.NextStep);

        var saved = Act("C6", ask.NextStep, ask.Draft, Cards());
        Assert.True(saved.Expense!.CreateCard);
        Assert.Equal("C6", saved.Expense.CardName);
        Assert.Null(saved.Expense.CardId);
        Assert.Equal(PaymentMethod.DebitCard, saved.Expense.Method);
        Assert.Equal(40m, saved.Expense.Amount);
    }

    [Fact]
    public void Repeated_card_name_reuses_the_existing_card()
    {
        var draft = new SessionDraft { Method = PaymentMethod.DebitCard, Amount = 15 };
        var saved = Act("inter", MenuStep.AwaitingNewCardName, draft, Cards());

        Assert.False(saved.Expense!.CreateCard);
        Assert.Equal(InterId, saved.Expense.CardId);
        Assert.Equal("Inter", saved.Expense.CardName);
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
        Assert.Equal(new DateOnly(2026, 10, 1), month.Summary!.From);
        Assert.Equal(new DateOnly(2026, 10, 31), month.Summary.To);
        Assert.Equal(MenuStep.SummaryMenu, month.NextStep);

        var year = Act("2", MenuStep.SummaryMenu);
        Assert.Equal(new DateOnly(2026, 1, 1), year.Summary!.From);
        Assert.Equal(new DateOnly(2026, 12, 31), year.Summary.To);

        var total = Act("3", MenuStep.SummaryMenu);
        Assert.Null(total.Summary!.From);
        Assert.Null(total.Summary.To);
        Assert.Equal("Resumo total", total.Summary.Title);

        var ask = Act("4", MenuStep.SummaryMenu);
        Assert.Equal(MenuStep.AwaitingDateRange, ask.NextStep);

        var range = Act("10/05/2026 - 20/05/2026", ask.NextStep);
        Assert.Equal(new DateOnly(2026, 5, 10), range.Summary!.From);
        Assert.Equal(new DateOnly(2026, 5, 20), range.Summary.To);

        var invalid = Act("ontem", MenuStep.AwaitingDateRange);
        Assert.Equal(MenuStep.AwaitingDateRange, invalid.NextStep);
        Assert.Null(invalid.Summary);
        Assert.Contains("Periodo invalido", invalid.Reply);
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

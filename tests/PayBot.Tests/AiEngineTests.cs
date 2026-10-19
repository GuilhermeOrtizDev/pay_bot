using PayBot.Ai;
using PayBot.Domain;
using PayBot.Menu;

namespace PayBot.Tests;

public class AiEngineTests
{
    private static readonly Guid NubankId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Free_text_in_ai_mode_needs_the_model()
    {
        Assert.Null(AiEngine.Direct("gastei 10 no pix", MenuStep.AiMode, new SessionDraft(), []));
        Assert.Null(AiEngine.Direct("1", MenuStep.AiMode, new SessionDraft(), []));
    }

    [Fact]
    public void Desativar_ia_leaves_ai_mode_from_any_ai_step()
    {
        foreach (var step in new[] { MenuStep.AiMode, MenuStep.AiAwaitingConfirm, MenuStep.AiAwaitingDeleteChoice })
        {
            var turn = AiEngine.Direct("Desativar IA.", step, new SessionDraft(), []);
            Assert.NotNull(turn);
            Assert.Equal(MenuStep.Main, turn.NextStep);
            Assert.Contains("Modo IA desativado", turn.Reply);
            Assert.EndsWith("8 - Modo IA", turn.Reply);
        }
    }

    [Fact]
    public void Missing_fields_are_asked_and_kept_for_the_next_message()
    {
        var first = Interpret(Expense(("tipo", "pix"), ("descricao", "mercado")), MenuStep.AiMode, new SessionDraft());
        Assert.Equal(MenuStep.AiMode, first.NextStep);
        Assert.Contains("valor", first.Reply);
        Assert.Null(first.Expense);

        var second = Interpret(Expense(("valor", "32,50")), first.NextStep, first.Draft);
        Assert.Equal(MenuStep.AiAwaitingConfirm, second.NextStep);
        Assert.Contains("Vou registrar: Gasto Pix R$ 32,50 - mercado", second.Reply);
        Assert.Null(second.Expense);
    }

    [Fact]
    public void Sim_saves_the_expense_without_the_menu()
    {
        var pending = Interpret(
            Expense(("tipo", "cartao credito"), ("cobranca", "parcelas"), ("parcelas", "10"), ("valor", "1200"), ("cartao", "nubank"), ("descricao", "notebook"), ("mes", "10/2026")),
            MenuStep.AiMode,
            new SessionDraft());
        Assert.Equal(MenuStep.AiAwaitingConfirm, pending.NextStep);

        var saved = AiEngine.Direct("Sim", pending.NextStep, pending.Draft, Cards());
        Assert.NotNull(saved);
        Assert.Equal(MenuStep.AiMode, saved.NextStep);
        Assert.NotNull(saved.Expense);
        Assert.Equal(PaymentMethod.CreditCard, saved.Expense.Method);
        Assert.Equal(1200m, saved.Expense.Amount);
        Assert.Equal(10, saved.Expense.Installments);
        Assert.Equal(NubankId, saved.Expense.CardId);
        Assert.False(saved.Expense.CreateCard);
        Assert.Equal(new DateOnly(2026, 10, 1), saved.Expense.ChargeMonth);
        Assert.StartsWith("Gasto registrado: Cartao credito Nubank R$ 1.200,00 em 10x", saved.Reply);
        Assert.DoesNotContain("1 - Registrar gasto", saved.Reply);
    }

    [Fact]
    public void Sim_saves_the_income()
    {
        var pending = Interpret(
            new AiIntent(AiAction.Income, Fields(("valor", "5000"), ("descricao", "salario"), ("mes", "10/2026"))),
            MenuStep.AiMode,
            new SessionDraft());
        Assert.Contains("Vou registrar: Entrada R$ 5.000,00 a partir de 10/2026 - salario", pending.Reply);

        var saved = AiEngine.Direct("sim", pending.NextStep, pending.Draft, Cards());
        Assert.NotNull(saved);
        Assert.NotNull(saved.Income);
        Assert.Null(saved.Expense);
        Assert.Equal(5000m, saved.Income.Amount);
        Assert.Equal(MenuStep.AiMode, saved.NextStep);
    }

    [Fact]
    public void Nao_discards_the_pending_entry()
    {
        var pending = Interpret(Expense(("tipo", "pix"), ("valor", "10"), ("descricao", "pao")), MenuStep.AiMode, new SessionDraft());

        var cancelled = AiEngine.Direct("não", pending.NextStep, pending.Draft, Cards());
        Assert.NotNull(cancelled);
        Assert.Equal(MenuStep.AiMode, cancelled.NextStep);
        Assert.Null(cancelled.Expense);
        Assert.Null(cancelled.Draft.Amount);
    }

    [Fact]
    public void Correction_while_confirming_patches_the_pending_entry()
    {
        var pending = Interpret(Expense(("tipo", "pix"), ("valor", "10"), ("descricao", "pao")), MenuStep.AiMode, new SessionDraft());
        Assert.Null(AiEngine.Direct("na verdade foi 12", pending.NextStep, pending.Draft, Cards()));

        var corrected = Interpret(Expense(("valor", "12")), pending.NextStep, pending.Draft);
        Assert.Equal(MenuStep.AiAwaitingConfirm, corrected.NextStep);
        Assert.Contains("R$ 12,00 - pao", corrected.Reply);
    }

    [Fact]
    public void Other_keeps_the_step_and_draft()
    {
        var pending = Interpret(Expense(("tipo", "pix"), ("valor", "10"), ("descricao", "pao")), MenuStep.AiMode, new SessionDraft());

        var other = Interpret(new AiIntent(AiAction.Other, Fields(), Reply: "Pode repetir?"), pending.NextStep, pending.Draft);
        Assert.Equal(MenuStep.AiAwaitingConfirm, other.NextStep);
        Assert.Equal(10m, other.Draft.Amount);
        Assert.Contains("Pode repetir?", other.Reply);
        Assert.Contains("sim ou nao", other.Reply);
    }

    [Fact]
    public void Summary_uses_the_period_and_format()
    {
        var month = Interpret(
            new AiIntent(AiAction.Summary, Fields(), new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 1), AsPdf: true),
            MenuStep.AiMode,
            new SessionDraft());
        Assert.NotNull(month.Summary);
        Assert.Equal(new DateOnly(2026, 10, 1), month.Summary.From);
        Assert.Equal(new DateOnly(2026, 10, 31), month.Summary.To);
        Assert.True(month.AsPdf);
        Assert.Equal(MenuStep.AiMode, month.NextStep);

        var total = Interpret(new AiIntent(AiAction.Summary, Fields()), MenuStep.AiMode, new SessionDraft());
        Assert.NotNull(total.Summary);
        Assert.Null(total.Summary.From);
        Assert.Equal("Resumo total", total.Summary.Title);
        Assert.False(total.AsPdf);
    }

    [Fact]
    public void Remove_lists_then_asks_for_the_number_then_confirms()
    {
        var list = Interpret(
            new AiIntent(AiAction.Remove, Fields(), new DateOnly(2026, 10, 12)),
            MenuStep.AiMode,
            new SessionDraft());
        Assert.Equal(MenuStep.AiAwaitingDeleteChoice, list.NextStep);
        Assert.NotNull(list.DeleteList);
        Assert.Equal(new DateOnly(2026, 10, 12), list.DeleteList.From);
        Assert.Equal(new DateOnly(2026, 10, 12), list.DeleteList.To);

        var recent = Interpret(new AiIntent(AiAction.Remove, Fields()), MenuStep.AiMode, new SessionDraft());
        Assert.Equal(10, recent.DeleteList!.Take);

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var shown = new SessionDraft { DeleteIds = [first, second] };

        var invalid = AiEngine.Direct("5", MenuStep.AiAwaitingDeleteChoice, shown, []);
        Assert.NotNull(invalid);
        Assert.Equal(MenuStep.AiAwaitingDeleteChoice, invalid.NextStep);

        var chosen = AiEngine.Direct("2", MenuStep.AiAwaitingDeleteChoice, shown, []);
        Assert.NotNull(chosen);
        Assert.Equal(MenuStep.AiAwaitingDeleteConfirm, chosen.NextStep);
        Assert.Null(chosen.DeleteId);
        Assert.Contains("registro 2", chosen.Reply);

        var deleted = AiEngine.Direct("sim", chosen.NextStep, chosen.Draft, []);
        Assert.NotNull(deleted);
        Assert.Equal(second, deleted.DeleteId);
        Assert.Equal(MenuStep.AiMode, deleted.NextStep);

        var kept = AiEngine.Direct("nao", chosen.NextStep, chosen.Draft, []);
        Assert.NotNull(kept);
        Assert.Null(kept.DeleteId);
    }

    [Fact]
    public void Parses_the_model_json()
    {
        var intent = OpenAiClient.ParseIntent("""
            {"acao":"gasto","campos":{"tipo":"cartao credito","cobranca":"parcelas","parcelas":10,"valor":"1200,00","cartao":"Nubank","descricao":"notebook","mes":"10/2026"},"de":null,"ate":null,"formato":"texto","resposta":null}
            """);
        Assert.NotNull(intent);
        Assert.Equal(AiAction.Expense, intent.Action);
        Assert.Equal("10", intent.Fields["parcelas"]);
        Assert.Equal("Nubank", intent.Fields["cartao"]);
        Assert.False(intent.AsPdf);

        var summary = OpenAiClient.ParseIntent("""
            {"acao":"resumo","campos":{"tipo":null,"cobranca":null,"parcelas":null,"valor":null,"cartao":null,"descricao":null,"mes":null},"de":"2026-10-01","ate":"2026-10-31","formato":"pdf","resposta":null}
            """);
        Assert.NotNull(summary);
        Assert.Equal(AiAction.Summary, summary.Action);
        Assert.Empty(summary.Fields);
        Assert.Equal(new DateOnly(2026, 10, 1), summary.From);
        Assert.True(summary.AsPdf);
    }

    private static MenuTurn Interpret(AiIntent intent, MenuStep step, SessionDraft draft) =>
        AiEngine.Interpret(intent, step, draft, Cards());

    private static AiIntent Expense(params (string Key, string Value)[] fields) =>
        new(AiAction.Expense, Fields(fields));

    private static IReadOnlyDictionary<string, string> Fields(params (string Key, string Value)[] fields) =>
        fields.ToDictionary(field => field.Key, field => field.Value);

    private static IReadOnlyList<KnownCard> Cards() =>
    [
        new KnownCard(NubankId, "Nubank", CardKind.Credit)
    ];
}

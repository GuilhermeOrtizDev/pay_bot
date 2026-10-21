using PayBot.Domain;
using PayBot.Menu;

namespace PayBot.Tests;

public class AdminEngineTests
{
    private static readonly AdminTenant Casa = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "Casa");
    private static readonly AdminTenant Empresa = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), "Empresa");
    private static readonly AdminGroup Familia = new(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"), "111@g.us", "Casa");
    private static readonly AdminGroup Loja = new(Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"), "222@g.us", null);

    [Fact]
    public void Secret_command_opens_admin_and_is_not_in_the_main_menu()
    {
        Assert.True(AdminEngine.IsCommand(" *000 "));
        Assert.DoesNotContain("*000", MenuTexts.MainMenu(true));

        var turn = Handle("*000", MenuStep.Main);
        Assert.Equal(MenuStep.AdminMenu, turn.NextStep);
        Assert.Equal(MenuTexts.Admin, turn.Reply);
    }

    [Fact]
    public void Zero_goes_back_to_the_main_menu()
    {
        var turn = Handle("0", MenuStep.AdminAwaitingTenantName);
        Assert.Equal(MenuStep.Main, turn.NextStep);
        Assert.Equal(MenuTexts.Main, turn.Reply);

        var ai = AdminEngine.Handle("0", MenuStep.AdminMenu, new SessionDraft(), [], [], aiAvailable: true);
        Assert.EndsWith("7 - Modo IA", ai.Reply);
    }

    [Fact]
    public void Registers_a_new_tenant()
    {
        var ask = Handle("1", MenuStep.AdminMenu);
        Assert.Equal(MenuStep.AdminAwaitingTenantName, ask.NextStep);

        var turn = Handle("  Viagem  ", ask.NextStep);
        Assert.Equal(MenuStep.AdminMenu, turn.NextStep);
        Assert.Equal("Viagem", turn.Admin?.NewTenantName);
        Assert.StartsWith("Tenant cadastrado: Viagem", turn.Reply);
    }

    [Fact]
    public void Rejects_duplicate_or_empty_tenant_names()
    {
        var duplicate = Handle("casa", MenuStep.AdminAwaitingTenantName);
        Assert.Equal(MenuStep.AdminAwaitingTenantName, duplicate.NextStep);
        Assert.Null(duplicate.Admin);
        Assert.StartsWith(MenuTexts.TenantExists, duplicate.Reply);

        var tooLong = Handle(new string('x', 81), MenuStep.AdminAwaitingTenantName);
        Assert.Null(tooLong.Admin);
        Assert.StartsWith(MenuTexts.InvalidTenantName, tooLong.Reply);
    }

    [Fact]
    public void Registers_a_group_jid_normalized()
    {
        var turn = Handle(" 333@G.US ", MenuStep.AdminAwaitingGroupJid);
        Assert.Equal(MenuStep.AdminMenu, turn.NextStep);
        Assert.Equal("333@g.us", turn.Admin?.NewGroupJid);
    }

    [Fact]
    public void Rejects_invalid_or_duplicate_group_jid()
    {
        foreach (var text in new[] { "5511999999999@s.whatsapp.net", "abc", "1 2@g.us" })
        {
            var turn = Handle(text, MenuStep.AdminAwaitingGroupJid);
            Assert.Equal(MenuStep.AdminAwaitingGroupJid, turn.NextStep);
            Assert.Null(turn.Admin);
            Assert.StartsWith(MenuTexts.InvalidGroupJid, turn.Reply);
        }

        var duplicate = Handle("111@g.us", MenuStep.AdminAwaitingGroupJid);
        Assert.Null(duplicate.Admin);
        Assert.StartsWith(MenuTexts.GroupExists, duplicate.Reply);
    }

    [Fact]
    public void Links_a_group_to_a_tenant()
    {
        var groups = Handle("3", MenuStep.AdminMenu);
        Assert.Equal(MenuStep.AdminAwaitingLinkGroup, groups.NextStep);
        Assert.Contains("1 - 111@g.us (tenant Casa)", groups.Reply);
        Assert.Contains("2 - 222@g.us (sem tenant)", groups.Reply);

        var tenants = Handle("2", groups.NextStep, groups.Draft);
        Assert.Equal(MenuStep.AdminAwaitingLinkTenant, tenants.NextStep);
        Assert.Equal(Loja.Id, tenants.Draft.AdminGroupId);
        Assert.Contains("1 - Casa", tenants.Reply);
        Assert.Contains("2 - Empresa", tenants.Reply);

        var linked = Handle("2", tenants.NextStep, tenants.Draft);
        Assert.Equal(MenuStep.AdminMenu, linked.NextStep);
        Assert.Equal(Loja.Id, linked.Admin?.LinkGroupId);
        Assert.Equal(Empresa.Id, linked.Admin?.LinkTenantId);
        Assert.StartsWith("Grupo vinculado: 222@g.us ao tenant Empresa", linked.Reply);
    }

    [Fact]
    public void Invalid_choice_keeps_the_list()
    {
        var groups = Handle("3", MenuStep.AdminMenu);
        var invalid = Handle("9", groups.NextStep, groups.Draft);
        Assert.Equal(MenuStep.AdminAwaitingLinkGroup, invalid.NextStep);
        Assert.Null(invalid.Admin);
        Assert.Contains("Escolha o grupo", invalid.Reply);
    }

    [Fact]
    public void Empty_lists_return_to_admin_menu()
    {
        var noGroups = AdminEngine.Handle("3", MenuStep.AdminMenu, new SessionDraft(), [Casa], []);
        Assert.Equal(MenuStep.AdminMenu, noGroups.NextStep);
        Assert.StartsWith(MenuTexts.NoGroups, noGroups.Reply);

        var draft = new SessionDraft { AdminIds = [Loja.Id] };
        var noTenants = AdminEngine.Handle("1", MenuStep.AdminAwaitingLinkGroup, draft, [], [Loja]);
        Assert.Equal(MenuStep.AdminMenu, noTenants.NextStep);
        Assert.StartsWith(MenuTexts.NoTenants, noTenants.Reply);
    }

    private static MenuTurn Handle(string text, MenuStep step, SessionDraft? draft = null) =>
        AdminEngine.Handle(text, step, draft ?? new SessionDraft(), [Casa, Empresa], [Familia, Loja]);
}

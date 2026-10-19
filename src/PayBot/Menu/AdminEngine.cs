using System.Globalization;
using PayBot.Domain;

namespace PayBot.Menu;

public static class AdminEngine
{
    public const string Command = "*000";

    public static bool IsAdminStep(MenuStep step) =>
        step is MenuStep.AdminMenu
            or MenuStep.AdminAwaitingTenantName
            or MenuStep.AdminAwaitingGroupJid
            or MenuStep.AdminAwaitingLinkGroup
            or MenuStep.AdminAwaitingLinkTenant;

    public static bool IsCommand(string? text) => (text ?? "").Trim() == Command;

    public static MenuTurn Handle(
        string? rawText,
        MenuStep step,
        SessionDraft current,
        IReadOnlyList<AdminTenant> tenants,
        IReadOnlyList<AdminGroup> groups,
        bool aiAvailable = false)
    {
        var text = (rawText ?? "").Trim();
        if (text == Command)
            return Open();
        if (text == "0")
            return new MenuTurn(MenuTexts.MainMenu(aiAvailable), MenuStep.Main, new SessionDraft(), null, null, null);

        return step switch
        {
            MenuStep.AdminAwaitingTenantName => OnTenantName(text, tenants),
            MenuStep.AdminAwaitingGroupJid => OnGroupJid(text, groups),
            MenuStep.AdminAwaitingLinkGroup => OnLinkGroup(text, current, tenants, groups),
            MenuStep.AdminAwaitingLinkTenant => OnLinkTenant(text, current, tenants, groups),
            _ => OnMenu(text, groups)
        };
    }

    private static MenuTurn OnMenu(string text, IReadOnlyList<AdminGroup> groups) => text switch
    {
        "1" => Stay(MenuStep.AdminAwaitingTenantName, new SessionDraft(), MenuTexts.AskTenantName),
        "2" => Stay(MenuStep.AdminAwaitingGroupJid, new SessionDraft(), MenuTexts.AskGroupJid),
        "3" when groups.Count == 0 => Back(MenuTexts.NoGroups),
        "3" => Stay(
            MenuStep.AdminAwaitingLinkGroup,
            new SessionDraft { AdminIds = groups.Select(group => group.Id).ToList() },
            MenuTexts.GroupList(groups)),
        _ => Open()
    };

    private static MenuTurn OnTenantName(string text, IReadOnlyList<AdminTenant> tenants)
    {
        if (text.Length is 0 or > 80)
            return Stay(MenuStep.AdminAwaitingTenantName, new SessionDraft(), MenuTexts.InvalidTenantName + "\n" + MenuTexts.AskTenantName);
        if (tenants.Any(tenant => string.Equals(tenant.Name.Trim(), text, StringComparison.OrdinalIgnoreCase)))
            return Stay(MenuStep.AdminAwaitingTenantName, new SessionDraft(), MenuTexts.TenantExists + "\n" + MenuTexts.AskTenantName);

        return Back($"Tenant cadastrado: {text}") with { Admin = new AdminChange(NewTenantName: text) };
    }

    private static MenuTurn OnGroupJid(string text, IReadOnlyList<AdminGroup> groups)
    {
        var jid = WhatsAppGroup.NormalizeJid(text);
        if (!IsGroupJid(jid))
            return Stay(MenuStep.AdminAwaitingGroupJid, new SessionDraft(), MenuTexts.InvalidGroupJid + "\n" + MenuTexts.AskGroupJid);
        if (groups.Any(group => group.Jid == jid))
            return Stay(MenuStep.AdminAwaitingGroupJid, new SessionDraft(), MenuTexts.GroupExists + "\n" + MenuTexts.AskGroupJid);

        return Back($"Grupo cadastrado: {jid} (sem tenant)") with { Admin = new AdminChange(NewGroupJid: jid) };
    }

    private static MenuTurn OnLinkGroup(
        string text,
        SessionDraft draft,
        IReadOnlyList<AdminTenant> tenants,
        IReadOnlyList<AdminGroup> groups)
    {
        var group = Pick(text, draft.AdminIds, groups, item => item.Id);
        if (group is null)
        {
            var listed = groups.Where(item => draft.AdminIds.Contains(item.Id)).ToList();
            return listed.Count == 0
                ? Back(MenuTexts.NoGroups)
                : Stay(MenuStep.AdminAwaitingLinkGroup, draft, MenuTexts.InvalidDeleteId + "\n" + MenuTexts.GroupList(listed));
        }

        if (tenants.Count == 0)
            return Back(MenuTexts.NoTenants);

        var next = new SessionDraft
        {
            AdminGroupId = group.Id,
            AdminIds = tenants.Select(tenant => tenant.Id).ToList()
        };
        return Stay(MenuStep.AdminAwaitingLinkTenant, next, MenuTexts.TenantList(tenants));
    }

    private static MenuTurn OnLinkTenant(
        string text,
        SessionDraft draft,
        IReadOnlyList<AdminTenant> tenants,
        IReadOnlyList<AdminGroup> groups)
    {
        var group = groups.FirstOrDefault(item => item.Id == draft.AdminGroupId);
        if (group is null)
            return Open();

        var tenant = Pick(text, draft.AdminIds, tenants, item => item.Id);
        if (tenant is null)
        {
            var listed = tenants.Where(item => draft.AdminIds.Contains(item.Id)).ToList();
            return listed.Count == 0
                ? Back(MenuTexts.NoTenants)
                : Stay(MenuStep.AdminAwaitingLinkTenant, draft, MenuTexts.InvalidDeleteId + "\n" + MenuTexts.TenantList(listed));
        }

        return Back($"Grupo vinculado: {group.Jid} ao tenant {tenant.Name}")
            with { Admin = new AdminChange(LinkGroupId: group.Id, LinkTenantId: tenant.Id) };
    }

    private static T? Pick<T>(string text, IReadOnlyList<Guid> ids, IReadOnlyList<T> items, Func<T, Guid> idOf)
        where T : class
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            || index < 1
            || index > ids.Count)
            return null;
        var id = ids[index - 1];
        return items.FirstOrDefault(item => idOf(item) == id);
    }

    private static bool IsGroupJid(string jid) =>
        jid.Length is > 5 and <= 128
        && jid.EndsWith("@g.us", StringComparison.Ordinal)
        && !jid.Any(char.IsWhiteSpace);

    private static MenuTurn Open() => Stay(MenuStep.AdminMenu, new SessionDraft(), MenuTexts.Admin);

    private static MenuTurn Back(string message) =>
        Stay(MenuStep.AdminMenu, new SessionDraft(), message + "\n\n" + MenuTexts.Admin);

    private static MenuTurn Stay(MenuStep step, SessionDraft draft, string reply) =>
        new(reply, step, draft, null, null, null);
}

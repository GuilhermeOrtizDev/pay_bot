using Microsoft.EntityFrameworkCore;
using PayBot.Domain;

namespace PayBot.Data;

public static class TenantSeeder
{
    public static async Task SeedAsync(PayBotDbContext db, string? groupJid, ILogger logger, CancellationToken cancellationToken)
    {
        var jid = WhatsAppGroup.NormalizeJid(groupJid);
        if (jid.Length == 0)
            return;
        if (await db.Groups.AnyAsync(group => group.Jid == jid, cancellationToken))
            return;
        if (!await db.Tenants.AnyAsync(tenant => tenant.Id == Tenant.DefaultId, cancellationToken))
        {
            logger.LogWarning("Tenant padrao ausente. GROUP_JID {Jid} nao foi cadastrado.", jid);
            return;
        }

        db.Groups.Add(new WhatsAppGroup
        {
            Id = Guid.NewGuid(),
            Jid = jid,
            TenantId = Tenant.DefaultId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("GROUP_JID {Jid} cadastrado no tenant padrao.", jid);
    }
}

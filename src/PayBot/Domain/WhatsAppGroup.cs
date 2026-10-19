namespace PayBot.Domain;

public sealed class WhatsAppGroup
{
    public Guid Id { get; set; }
    public string Jid { get; set; } = "";
    public Guid? TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static string NormalizeJid(string? jid) => (jid ?? "").Trim().ToLowerInvariant();
}

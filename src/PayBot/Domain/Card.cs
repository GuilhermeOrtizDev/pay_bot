namespace PayBot.Domain;

public sealed class Card
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public CardKind Kind { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
}

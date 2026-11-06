namespace PayBot.Domain;

public sealed class FixedExpense
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = "";
    public PaymentMethod Method { get; set; } = PaymentMethod.Fixed;
    public Guid? CardId { get; set; }
    public Card? Card { get; set; }
    public DateOnly StartMonth { get; set; }
    public DateOnly? EndMonth { get; set; }
    public string WhatsAppMessageId { get; set; } = "";
}

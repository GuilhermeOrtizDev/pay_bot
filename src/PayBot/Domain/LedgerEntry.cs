namespace PayBot.Domain;

public sealed class LedgerEntry
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateOnly LocalDate { get; set; }
    public Direction Direction { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }
    public string? Description { get; set; }
    public int? Installments { get; set; }
    public bool IsFixed { get; set; }
    public Guid? CardId { get; set; }
    public Card? Card { get; set; }
    public string WhatsAppMessageId { get; set; } = "";
}

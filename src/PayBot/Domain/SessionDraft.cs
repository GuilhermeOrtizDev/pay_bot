namespace PayBot.Domain;

public sealed class SessionDraft
{
    public PaymentMethod? Method { get; set; }
    public decimal? Amount { get; set; }
    public int? Installments { get; set; }
    public CardBilling? Billing { get; set; }
    public Guid? CardId { get; set; }
    public string? CardName { get; set; }
    public bool CreateCard { get; set; }
    public List<Guid> DeleteIds { get; set; } = [];

    public SessionDraft Clone() => new()
    {
        Method = Method,
        Amount = Amount,
        Installments = Installments,
        Billing = Billing,
        CardId = CardId,
        CardName = CardName,
        CreateCard = CreateCard,
        DeleteIds = (DeleteIds ?? []).ToList()
    };
}

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
    public DateOnly? SummaryFrom { get; set; }
    public DateOnly? SummaryTo { get; set; }
    public string? SummaryTitle { get; set; }
    public string? Description { get; set; }
    public DateOnly? ChargeMonth { get; set; }
    public bool IsBulkIncome { get; set; }

    public SessionDraft Clone() => new()
    {
        Method = Method,
        Amount = Amount,
        Installments = Installments,
        Billing = Billing,
        CardId = CardId,
        CardName = CardName,
        CreateCard = CreateCard,
        DeleteIds = (DeleteIds ?? []).ToList(),
        SummaryFrom = SummaryFrom,
        SummaryTo = SummaryTo,
        SummaryTitle = SummaryTitle,
        Description = Description,
        ChargeMonth = ChargeMonth,
        IsBulkIncome = IsBulkIncome
    };
}

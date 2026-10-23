namespace PayBot.Domain;

public sealed class SessionDraft
{
    public PaymentMethod? Method { get; set; }
    public decimal? Amount { get; set; }
    public int? Installments { get; set; }
    public InstallmentAmountKind? AmountKind { get; set; }
    public CardBilling? Billing { get; set; }
    public Guid? CardId { get; set; }
    public string? CardName { get; set; }
    public bool CreateCard { get; set; }
    public List<Guid> DeleteIds { get; set; } = [];
    public DateOnly? SummaryFrom { get; set; }
    public DateOnly? SummaryTo { get; set; }
    public string? SummaryTitle { get; set; }
    public PaymentMethod? SummaryMethod { get; set; }
    public Guid? SummaryCardId { get; set; }
    public string? SummaryCardName { get; set; }
    public string? Description { get; set; }
    public DateOnly? ChargeMonth { get; set; }
    public bool IsBulkIncome { get; set; }
    public List<Guid> AdminIds { get; set; } = [];
    public Guid? AdminGroupId { get; set; }

    public SessionDraft Clone() => new()
    {
        Method = Method,
        Amount = Amount,
        Installments = Installments,
        AmountKind = AmountKind,
        Billing = Billing,
        CardId = CardId,
        CardName = CardName,
        CreateCard = CreateCard,
        DeleteIds = (DeleteIds ?? []).ToList(),
        SummaryFrom = SummaryFrom,
        SummaryTo = SummaryTo,
        SummaryTitle = SummaryTitle,
        SummaryMethod = SummaryMethod,
        SummaryCardId = SummaryCardId,
        SummaryCardName = SummaryCardName,
        Description = Description,
        ChargeMonth = ChargeMonth,
        IsBulkIncome = IsBulkIncome,
        AdminIds = (AdminIds ?? []).ToList(),
        AdminGroupId = AdminGroupId
    };
}

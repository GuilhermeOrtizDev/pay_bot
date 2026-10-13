namespace PayBot.Domain;

public sealed class SessionDraft
{
    public PaymentMethod? Method { get; set; }
    public decimal? Amount { get; set; }
    public int? Installments { get; set; }

    public SessionDraft Clone() => new()
    {
        Method = Method,
        Amount = Amount,
        Installments = Installments
    };
}

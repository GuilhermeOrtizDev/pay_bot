namespace PayBot.Domain;

public sealed class ProcessedMessage
{
    public string MessageId { get; set; } = "";
    public DateTimeOffset ProcessedAt { get; set; }
}

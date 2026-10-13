namespace PayBot.Domain;

public sealed class ConversationSession
{
    public Guid Id { get; set; }
    public string GroupJid { get; set; } = "";
    public MenuStep Step { get; set; }
    public string DraftJson { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
}

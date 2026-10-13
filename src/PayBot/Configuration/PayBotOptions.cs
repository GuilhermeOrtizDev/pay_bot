namespace PayBot.Configuration;

public sealed class PayBotOptions
{
    public const string SectionName = "PayBot";

    public string EvolutionBaseUrl { get; set; } = "http://evolution:8080";
    public string EvolutionApiKey { get; set; } = "";
    public string EvolutionInstance { get; set; } = "paybot";
    public string WebhookUrl { get; set; } = "http://paybot:8080/webhooks/evolution";
    public string GroupJid { get; set; } = "";
    public string OwnerNumber { get; set; } = "";
}

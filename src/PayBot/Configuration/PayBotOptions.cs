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
    public bool AiEnabled { get; set; }
    public bool AiTranscribeEnabled { get; set; }
    public string OpenAiApiKey { get; set; } = "";
    public string OpenAiBaseUrl { get; set; } = "https://api.openai.com";
    public string OpenAiModel { get; set; } = "gpt-4o-mini";
    public string OpenAiTranscribeModel { get; set; } = "gpt-transcribe";

    public bool AiAvailable => AiEnabled && !string.IsNullOrWhiteSpace(OpenAiApiKey);
    public bool TranscribeAvailable => AiAvailable && AiTranscribeEnabled;
}

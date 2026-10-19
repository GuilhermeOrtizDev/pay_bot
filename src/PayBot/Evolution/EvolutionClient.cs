using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using PayBot.Configuration;
using PayBot.Webhooks;

namespace PayBot.Evolution;

public sealed class EvolutionClient(
    HttpClient http,
    IOptions<PayBotOptions> options,
    ILogger<EvolutionClient> logger)
{
    private readonly PayBotOptions _options = options.Value;

    public async Task SendAsync(string groupJid, string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(groupJid) || string.IsNullOrWhiteSpace(_options.EvolutionApiKey))
        {
            logger.LogWarning("Envio ignorado: grupo ou chave da Evolution ausente.");
            return;
        }

        var status = await PostAsync(
            $"/message/sendText/{Uri.EscapeDataString(_options.EvolutionInstance)}",
            new { number = groupJid.Trim(), text = BotEcho.Marker + text },
            cancellationToken);

        if (!IsSuccess(status))
            logger.LogWarning("Evolution recusou o envio com status {Status}.", status);
    }

    public async Task<bool> SendDocumentAsync(
        string groupJid,
        byte[] content,
        string fileName,
        string caption,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(groupJid) || string.IsNullOrWhiteSpace(_options.EvolutionApiKey))
        {
            logger.LogWarning("PDF ignorado: grupo ou chave da Evolution ausente.");
            return false;
        }

        var status = await PostAsync(
            $"/message/sendMedia/{Uri.EscapeDataString(_options.EvolutionInstance)}",
            new
            {
                number = groupJid.Trim(),
                mediatype = "document",
                mimetype = "application/pdf",
                caption,
                fileName,
                media = Convert.ToBase64String(content)
            },
            cancellationToken);

        if (!IsSuccess(status))
            logger.LogWarning("Evolution recusou o PDF com status {Status}.", status);
        return IsSuccess(status);
    }

    public async Task<(byte[] Content, string? MimeType)?> GetMediaBase64Async(
        string rawMessageJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.EvolutionApiKey))
            return null;

        // The full webhook message is sent because Evolution does not store messages here (DATABASE_SAVE_DATA_NEW_MESSAGE=false).
        var body = new JsonObject
        {
            ["message"] = JsonNode.Parse(rawMessageJson),
            ["convertToMp4"] = false
        };
        var url = _options.EvolutionBaseUrl.TrimEnd('/')
            + $"/chat/getBase64FromMediaMessage/{Uri.EscapeDataString(_options.EvolutionInstance)}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("apikey", _options.EvolutionApiKey);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Evolution recusou o download da midia com status {Status}.", (int)response.StatusCode);
                return null;
            }

            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var base64 = json?["base64"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(base64))
                return null;

            return (Convert.FromBase64String(base64), json?["mimetype"]?.GetValue<string>());
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or FormatException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Falha ao baixar a midia na Evolution.");
            return null;
        }
    }

    public async Task<bool> EnsureWebhookAsync(CancellationToken cancellationToken)
    {
        var path = $"/webhook/set/{Uri.EscapeDataString(_options.EvolutionInstance)}";
        var nested = await PostAsync(path, new
        {
            webhook = new
            {
                enabled = true,
                url = _options.WebhookUrl,
                byEvents = false,
                base64 = false,
                events = new[] { "MESSAGES_UPSERT" }
            }
        }, cancellationToken);

        if (IsSuccess(nested))
            return true;

        var flat = await PostAsync(path, new
        {
            enabled = true,
            url = _options.WebhookUrl,
            webhookByEvents = false,
            webhookBase64 = false,
            events = new[] { "MESSAGES_UPSERT" }
        }, cancellationToken);

        if (IsSuccess(flat))
            return true;

        logger.LogWarning(
            "Webhook da Evolution nao aplicado. Status aninhado {Nested} e plano {Flat}.",
            nested,
            flat);
        return false;
    }

    private async Task<int> PostAsync(string path, object body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.EvolutionApiKey))
            return 0;

        var url = _options.EvolutionBaseUrl.TrimEnd('/') + path;
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("apikey", _options.EvolutionApiKey);
        request.Content = JsonContent.Create(body, options: JsonSerializerOptions.Web);
        using var response = await http.SendAsync(request, cancellationToken);
        return (int)response.StatusCode;
    }

    private static bool IsSuccess(int status) => status is >= 200 and < 300;
}

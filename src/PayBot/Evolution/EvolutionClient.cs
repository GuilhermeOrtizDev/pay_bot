using System.Net.Http.Json;
using System.Text.Json;
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

    public async Task SendAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.GroupJid) || string.IsNullOrWhiteSpace(_options.EvolutionApiKey))
        {
            logger.LogWarning("Envio ignorado: grupo ou chave da Evolution ausente.");
            return;
        }

        var status = await PostAsync(
            $"/message/sendText/{Uri.EscapeDataString(_options.EvolutionInstance)}",
            new { number = _options.GroupJid.Trim(), text = BotEcho.Marker + text },
            cancellationToken);

        if (!IsSuccess(status))
            logger.LogWarning("Evolution recusou o envio com status {Status}.", status);
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

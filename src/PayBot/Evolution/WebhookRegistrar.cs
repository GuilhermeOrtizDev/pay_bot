using Microsoft.Extensions.Options;
using PayBot.Configuration;

namespace PayBot.Evolution;

public sealed class WebhookRegistrar(
    EvolutionClient evolution,
    IOptions<PayBotOptions> options,
    ILogger<WebhookRegistrar> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.EvolutionApiKey))
        {
            logger.LogWarning("Chave da Evolution vazia. O webhook nao sera configurado.");
            return;
        }

        var delay = TimeSpan.FromSeconds(5);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, stoppingToken);
                var applied = await evolution.EnsureWebhookAsync(stoppingToken);
                delay = applied ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(15);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                delay = TimeSpan.FromSeconds(15);
                logger.LogWarning(exception, "Falha ao configurar o webhook da Evolution.");
            }
        }
    }
}

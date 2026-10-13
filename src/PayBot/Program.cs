using Microsoft.EntityFrameworkCore;
using PayBot.Configuration;
using PayBot.Data;
using PayBot.Evolution;
using PayBot.Summaries;
using PayBot.Webhooks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PayBotOptions>(builder.Configuration.GetSection(PayBotOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("PayBot")
    ?? throw new InvalidOperationException("Connection string PayBot ausente.");

builder.Services.AddDbContext<PayBotDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddHttpClient<EvolutionClient>(client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddSingleton<InboundGate>();
builder.Services.AddScoped<SummaryService>();
builder.Services.AddScoped<InboundProcessor>();
builder.Services.AddHostedService<WebhookRegistrar>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PayBotDbContext>();
    await db.Database.MigrateAsync();
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapPost("/webhooks/evolution", async (HttpRequest request, InboundProcessor processor, CancellationToken cancellationToken) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync(cancellationToken);
    var headerKey = request.Headers.TryGetValue("apikey", out var value) ? value.ToString() : null;
    var result = await processor.HandleAsync(body, headerKey, cancellationToken);
    return result == WebhookResult.Unauthorized ? Results.Unauthorized() : Results.Ok();
});

app.Run();

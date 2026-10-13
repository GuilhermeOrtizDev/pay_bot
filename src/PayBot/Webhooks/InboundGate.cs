namespace PayBot.Webhooks;

public enum WebhookResult
{
    Ok,
    Unauthorized
}

public sealed class InboundGate
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task WaitAsync(CancellationToken cancellationToken) => _gate.WaitAsync(cancellationToken);

    public void Release() => _gate.Release();
}

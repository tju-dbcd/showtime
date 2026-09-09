namespace ShowtimeBackend.Services.OrderTicket.Messaging;

internal sealed class RabbitMqConsumerLifetime
{
    private readonly TaskCompletionSource _ended = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public void SignalEnded() => _ended.TrySetResult();

    public Task WaitAsync(CancellationToken cancellationToken) =>
        _ended.Task.WaitAsync(cancellationToken);
}

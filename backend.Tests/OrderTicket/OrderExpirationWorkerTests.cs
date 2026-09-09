using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class OrderExpirationWorkerTests
{
    [Fact]
    public void ApplicationServices_RegisterOrderExpirationWorker()
    {
        using var factory = new AuthTestFactory(enableOrderExpirationWorker: true);
        using var client = factory.CreateApiClient();

        Assert.Contains(
            factory.Services.GetServices<IHostedService>(),
            service => service is OrderExpirationWorker);
    }

    [Fact]
    public async Task Worker_StartsWithImmediateBacklogScanAndFullBatchAdvancesCursor()
    {
        var probe = new ExpirationProbe(
            expectedCalls: 2,
            [Batch(2, lastOrderId: 20), Batch(1, lastOrderId: 21)]);
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddScoped<IOrderExpirationService, ProbeExpirationService>();
        await using var provider = services.BuildServiceProvider();
        using var worker = new OrderExpirationWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            Options.Create(new OrderExpirationOptions
            {
                ExpirationScanIntervalSeconds = 3_600,
                ExpirationBatchSize = 2,
            }),
            NullLogger<OrderExpirationWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await probe.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, probe.CallCount);
        Assert.Equal(new long?[] { null, 20 }, probe.Cursors);
    }

    [Fact]
    public async Task Worker_TopLevelFailureIsRetriedAndStopCancelsWaiting()
    {
        var probe = new ExpirationProbe(
            expectedCalls: 2,
            [new InvalidOperationException("injected scan failure"), Batch(0)]);
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddScoped<IOrderExpirationService, ProbeExpirationService>();
        await using var provider = services.BuildServiceProvider();
        using var worker = new OrderExpirationWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            Options.Create(new OrderExpirationOptions
            {
                ExpirationScanIntervalSeconds = 1,
                ExpirationBatchSize = 10,
            }),
            NullLogger<OrderExpirationWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await probe.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);
        var countAfterStop = probe.CallCount;
        await Task.Delay(50);

        Assert.Equal(2, countAfterStop);
        Assert.Equal(countAfterStop, probe.CallCount);
    }

    private static OrderExpirationBatchResult Batch(int candidates, long? lastOrderId = null) =>
        new(candidates, candidates, 0, 0, lastOrderId);

    private sealed class ExpirationProbe(
        int expectedCalls,
        IEnumerable<object> results)
    {
        private int callCount;
        private readonly Queue<object> queuedResults = new(results);
        private readonly List<long?> cursors = [];

        public int CallCount => Volatile.Read(ref callCount);
        public IReadOnlyList<long?> Cursors
        {
            get
            {
                lock (cursors)
                    return cursors.ToArray();
            }
        }

        public TaskCompletionSource Invoked { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public OrderExpirationBatchResult Invoke(long? afterOrderId)
        {
            lock (cursors)
                cursors.Add(afterOrderId);
            var current = Interlocked.Increment(ref callCount);
            if (current >= expectedCalls)
                Invoked.TrySetResult();
            object result;
            lock (queuedResults)
                result = queuedResults.Dequeue();
            return result is Exception exception
                ? throw exception
                : (OrderExpirationBatchResult)result;
        }
    }

    private sealed class ProbeExpirationService(ExpirationProbe probe)
        : IOrderExpirationService
    {
        public Task<OrderExpirationBatchResult> ExpireDueBatchAsync(
            long? afterOrderId = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(probe.Invoke(afterOrderId));
        }

        public Task<OrderExpirationOutcome> ExpireOrderAsync(
            long orderId,
            string actor,
            DateTime now,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

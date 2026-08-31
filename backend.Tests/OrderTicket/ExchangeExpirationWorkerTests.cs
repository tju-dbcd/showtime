using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Common.TicketSecurity;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class ExchangeExpirationWorkerTests
{
    [Fact]
    public void ApplicationServices_RegisterExchangeExpirationWorker()
    {
        using var factory = new AuthTestFactory();
        using var client = factory.CreateApiClient();

        Assert.Contains(
            factory.Services.GetServices<IHostedService>(),
            service => service is ExchangeExpirationWorker);
    }

    [Fact]
    public async Task Worker_AutomaticallyInvokesScopedExpirationService()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
        var probe = new ExpirationProbe(expectedCalls: 1, [() => Batch(0)]);
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddScoped<IExchangeExpirationService, ProbeExpirationService>();
        await using var provider = services.BuildServiceProvider();
        using var worker = new ExchangeExpirationWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            timeProvider,
            Options.Create(new ExchangeOptions
            {
                ExpirationScanIntervalSeconds = 1,
                ExpirationBatchSize = 10,
            }),
            NullLogger<ExchangeExpirationWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitForTimerCountAsync(timeProvider, 1);
            timeProvider.Advance(TimeSpan.FromSeconds(1));
            await probe.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        Assert.True(probe.CallCount >= 1);
    }

    [Fact]
    public async Task Worker_FullBatchContinuesImmediatelyAndStopCancelsFutureScans()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
        var probe = new ExpirationProbe(
            expectedCalls: 2,
            [() => Batch(10, lastExchangeId: 10), () => Batch(0)]);
        await using var provider = BuildProvider(probe);
        using var worker = CreateWorker(provider, timeProvider, batchSize: 10);

        await worker.StartAsync(CancellationToken.None);
        await WaitForTimerCountAsync(timeProvider, 1);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await probe.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, probe.CallCount);
        Assert.Equal(new long?[] { null, 10 }, probe.Cursors);

        await worker.StopAsync(CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(20);
        Assert.Equal(2, probe.CallCount);
    }

    [Fact]
    public async Task Worker_FullBatchWithFailure_StillAdvancesPastPoisonCandidate()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
        var probe = new ExpirationProbe(
            expectedCalls: 2,
            [
                () => Batch(2, success: 1, failure: 1, lastExchangeId: 20),
                () => Batch(1, success: 1, lastExchangeId: 21),
            ]);
        await using var provider = BuildProvider(probe);
        using var worker = CreateWorker(provider, timeProvider, batchSize: 2);

        await worker.StartAsync(CancellationToken.None);
        await WaitForTimerCountAsync(timeProvider, 1);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await probe.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(2, probe.CallCount);
        Assert.Equal(new long?[] { null, 20 }, probe.Cursors);
    }

    [Fact]
    public async Task Worker_ScanExceptionIsolatedAndRetriedOnNextInterval()
    {
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero));
        var probe = new ExpirationProbe(
            expectedCalls: 2,
            [() => throw new InvalidOperationException("injected scan failure"), () => Batch(0)]);
        await using var provider = BuildProvider(probe);
        using var worker = CreateWorker(provider, timeProvider, batchSize: 10);

        await worker.StartAsync(CancellationToken.None);
        await WaitForTimerCountAsync(timeProvider, 1);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await WaitForCallCountAsync(probe, 1);
        await WaitForTimerCountAsync(timeProvider, 2);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        await probe.Invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, probe.CallCount);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Worker_WithRealScopedServices_RestoresReviewAndPaymentExpirations()
    {
        await using var source = await ExchangeQuoteServiceTests.CreateFixtureAsync(
            [105m, 110m], [125m, 130m], fee: 5m);
        var application = new ExchangeApplicationService(
            source.Db,
            new ExchangePolicyEngine(),
            source.TimeProvider,
            new OracleExchangeLockCoordinator(source.Db));
        var review = new ExchangeReviewService(
            source.Db,
            source.TimeProvider,
            new OracleExchangeLockCoordinator(source.Db),
            application,
            Options.Create(new ExchangeOptions()),
            new TicketIssuanceService(new WorkerTicketTokenService()));
        var pending = await application.CreateAsync(
            7,
            "worker-user",
            11,
            new CreateExchangeRequest(22, [new(101, 701, 801, "lock-701")], null));
        var processing = await application.CreateAsync(
            7,
            "worker-user",
            11,
            new CreateExchangeRequest(22, [new(102, 702, 802, "lock-702")], null));
        Assert.True(pending.IsSuccess, pending.Message);
        Assert.True(processing.IsSuccess, processing.Message);
        var approved = await review.ApproveAsync(
            "worker-admin",
            processing.Value!.ExchangeId,
            new ApproveExchangeRequest(null));
        Assert.True(approved.IsSuccess, approved.Message);
        var childIds = new[] { pending.Value!.ChildOrderId, processing.Value.ChildOrderId };
        await source.Db.Set<Order>()
            .Where(item => childIds.Contains(item.OrderId))
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                item => item.ExpireTime,
                RefundTestData.FixedUtcNow));
        source.Db.ChangeTracker.Clear();

        var databaseName = $"exchange-worker-{Guid.NewGuid():N}";
        var connectionString =
            $"Data Source={databaseName};Mode=Memory;Cache=Shared;Default Timeout=30;" +
            "Pooling=False;Foreign Keys=False";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        source.BackupTo(keeper);
        var timeProvider = new ManualTimeProvider(
            new DateTimeOffset(RefundTestData.FixedUtcNow));
        await using var provider = BuildRealProvider(connectionString, timeProvider);
        using var worker = CreateWorker(provider, timeProvider, batchSize: 1);

        await worker.StartAsync(CancellationToken.None);
        await WaitForTimerCountAsync(timeProvider, 1);
        timeProvider.Advance(TimeSpan.FromSeconds(1));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            await using var verification = CreateSharedContext(connectionString);
            var failed = await verification.Set<ExchangeRequest>().AsNoTracking()
                .CountAsync(item => item.ExchangeStatus == "FAILED", timeout.Token);
            if (failed == 2)
                break;
            await Task.Delay(20, timeout.Token);
        }
        await worker.StopAsync(CancellationToken.None);

        await using var finalDb = CreateSharedContext(connectionString);
        var exchanges = await finalDb.Set<ExchangeRequest>().AsNoTracking()
            .OrderBy(item => item.ExchangeId)
            .ToListAsync();
        Assert.Equal(2, exchanges.Count);
        Assert.Contains(exchanges, item => item.ApproveStatus == "REJECTED");
        Assert.Contains(exchanges, item => item.ApproveStatus == "APPROVED");
        Assert.All(exchanges, item => Assert.Equal("FAILED", item.ExchangeStatus));
        Assert.All(
            await finalDb.Set<ETicket>().AsNoTracking()
                .Where(item => item.OrderItemId == 101 || item.OrderItemId == 102)
                .ToListAsync(),
            item => Assert.Equal("UNUSED", item.TicketStatus));
    }

    private static ServiceProvider BuildProvider(ExpirationProbe probe)
    {
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddScoped<IExchangeExpirationService, ProbeExpirationService>();
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildRealProvider(
        string connectionString,
        TimeProvider timeProvider)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(timeProvider);
        services.AddSingleton<IOptions<ExchangeOptions>>(Options.Create(new ExchangeOptions
        {
            ExpirationScanIntervalSeconds = 1,
            ExpirationBatchSize = 1,
        }));
        services.AddSingleton<ITicketIssuanceService>(
            new TicketIssuanceService(new WorkerTicketTokenService()));
        services.AddSingleton<ExchangePolicyEngine>();
        services.AddScoped<AppDbContext>(_ => CreateSharedContext(connectionString));
        services.AddScoped<IExchangeLockCoordinator, OracleExchangeLockCoordinator>();
        services.AddScoped<IExchangeApplicationService, ExchangeApplicationService>();
        services.AddScoped<IExchangeReviewService, ExchangeReviewService>();
        services.AddScoped<IExchangeExpirationService, ExchangeExpirationService>();
        return services.BuildServiceProvider();
    }

    private static AppDbContext CreateSharedContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<SqliteAuthDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new SqliteAuthDbContext(options);
    }

    private static ExchangeExpirationWorker CreateWorker(
        ServiceProvider provider,
        TimeProvider timeProvider,
        int batchSize) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        timeProvider,
        Options.Create(new ExchangeOptions
        {
            ExpirationScanIntervalSeconds = 1,
            ExpirationBatchSize = batchSize,
        }),
        NullLogger<ExchangeExpirationWorker>.Instance);

    private static async Task WaitForCallCountAsync(
        ExpirationProbe probe,
        int expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (probe.CallCount < expected)
            await Task.Delay(10, timeout.Token);
    }

    private static async Task WaitForTimerCountAsync(
        ManualTimeProvider timeProvider,
        int expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (timeProvider.TimerCount < expected)
            await Task.Delay(10, timeout.Token);
    }

    private sealed class ExpirationProbe(
        int expectedCalls,
        IEnumerable<Func<ExchangeExpirationBatchResult>> results)
    {
        private int callCount;
        private readonly Queue<Func<ExchangeExpirationBatchResult>> queuedResults = new(results);
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

        public ExchangeExpirationBatchResult Invoke(long? afterExchangeId)
        {
            lock (cursors)
                cursors.Add(afterExchangeId);
            var current = Interlocked.Increment(ref callCount);
            if (current >= expectedCalls)
                Invoked.TrySetResult();
            Func<ExchangeExpirationBatchResult> result;
            lock (queuedResults)
                result = queuedResults.Count > 0 ? queuedResults.Dequeue() : () => Batch(0);
            return result();
        }
    }

    private sealed class ProbeExpirationService(ExpirationProbe probe)
        : IExchangeExpirationService
    {
        public Task<ExchangeExpirationBatchResult> ExpireDueBatchAsync(
            long? afterExchangeId = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(probe.Invoke(afterExchangeId));
        }
    }

    private static ExchangeExpirationBatchResult Batch(
        int candidates,
        int success = 0,
        int failure = 0,
        long? lastExchangeId = null) =>
        new(candidates, success, failure, lastExchangeId);

    private sealed class ManualTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private readonly object sync = new();
        private readonly List<ManualTimer> timers = [];
        private DateTimeOffset utcNow = initialUtc;

        public override DateTimeOffset GetUtcNow()
        {
            lock (sync)
                return utcNow;
        }

        public override long GetTimestamp() => GetUtcNow().UtcTicks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public int TimerCount
        {
            get
            {
                lock (sync)
                    return timers.Count;
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state, dueTime, period);
            lock (sync)
                timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan amount)
        {
            ManualTimer[] snapshot;
            lock (sync)
            {
                utcNow += amount;
                snapshot = timers.ToArray();
            }
            foreach (var timer in snapshot)
                timer.FireIfDue();
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly ManualTimeProvider provider;
            private readonly TimerCallback callback;
            private readonly object? state;
            private readonly object sync = new();
            private DateTimeOffset? dueAt;
            private TimeSpan repeat;
            private bool disposed;

            public ManualTimer(
                ManualTimeProvider provider,
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                this.provider = provider;
                this.callback = callback;
                this.state = state;
                dueAt = DueAt(dueTime);
                repeat = period;
            }

            public bool Change(TimeSpan newDueTime, TimeSpan newPeriod)
            {
                lock (sync)
                {
                    if (disposed) return false;
                    dueAt = DueAt(newDueTime);
                    repeat = newPeriod;
                    return true;
                }
            }

            public void FireIfDue()
            {
                var shouldFire = false;
                lock (sync)
                {
                    if (!disposed && dueAt.HasValue && provider.GetUtcNow() >= dueAt.Value)
                    {
                        shouldFire = true;
                        dueAt = repeat == Timeout.InfiniteTimeSpan
                            ? null
                            : provider.GetUtcNow() + repeat;
                    }
                }
                if (shouldFire)
                    callback(state);
            }

            public void Dispose()
            {
                lock (sync)
                    disposed = true;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            private DateTimeOffset? DueAt(TimeSpan value) =>
                value == Timeout.InfiniteTimeSpan ? null : provider.GetUtcNow() + value;
        }
    }

    private sealed class WorkerTicketTokenService : ITicketTokenService
    {
        private int sequence;

        public TicketCredential Generate(DateTimeOffset issuedAt)
        {
            var value = Interlocked.Increment(ref sequence);
            return new TicketCredential(
                $"WORKER-TKT-{value}",
                $"worker-anti-{value}",
                $"worker-qr-{value}");
        }

        public bool TryValidate(string qrCode, out TicketTokenPayload? payload)
        {
            payload = null;
            return false;
        }
    }
}

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Services.OrderTicket.Messaging;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class OrderEventOutboxServiceTests
{
    [Fact]
    public async Task WorkerProcessesBacklogImmediatelyAndStopsWithCancellation()
    {
        var scanner = new RecordingOutboxService();
        var services = new ServiceCollection()
            .AddScoped<IOrderEventOutboxService>(_ => scanner)
            .BuildServiceProvider();
        var worker = new OrderEventOutboxWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new RabbitMqOptions { OutboxPollIntervalSeconds = 3600 }),
            NullLogger<OrderEventOutboxWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await scanner.Called.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, scanner.CallCount);
        worker.Dispose();
        await services.DisposeAsync();
    }

    [Fact]
    public async Task PublishConfirmationCompletesBeforeMessageIsMarkedPublished()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync();
        var publisher = new BlockingPublisher();
        var service = fixture.CreateService(publisher);

        var processing = service.ProcessBatchAsync(CancellationToken.None);
        await publisher.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var beforeConfirmation = await fixture.ReadSingleAsync();
        Assert.Equal("PROCESSING", beforeConfirmation.Status);
        Assert.Null(beforeConfirmation.PublishedAt);

        publisher.Release.TrySetResult();
        var result = await processing.WaitAsync(TimeSpan.FromSeconds(5));
        var afterConfirmation = await fixture.ReadSingleAsync();
        Assert.Equal(1, result.Published);
        Assert.Equal("PUBLISHED", afterConfirmation.Status);
        Assert.NotNull(afterConfirmation.PublishedAt);
    }

    [Fact]
    public async Task ConcurrentClaimAllowsOnlyOneWorkerToPublish()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync();
        var blocking = new BlockingPublisher();
        var first = fixture.CreateService(blocking);
        var losingPublisher = new RecordingPublisher();
        var second = fixture.CreateService(losingPublisher);

        var firstTask = first.ProcessBatchAsync(CancellationToken.None);
        await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondResult = await second.ProcessBatchAsync(CancellationToken.None);
        blocking.Release.TrySetResult();
        var firstResult = await firstTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, firstResult.Claimed);
        Assert.Equal(0, secondResult.Claimed);
        Assert.Empty(losingPublisher.Messages);
    }

    [Fact]
    public async Task PublishFailuresBackOffAndEventuallyBecomeFailed()
    {
        await using var fixture = await Fixture.CreateAsync(maxAttempts: 2);
        await fixture.SeedAsync();
        var service = fixture.CreateService(new ThrowingPublisher());

        var first = await service.ProcessBatchAsync(CancellationToken.None);
        var pending = await fixture.ReadSingleAsync();
        Assert.Equal(1, first.Retried);
        Assert.Equal("PENDING", pending.Status);
        Assert.Equal(1, pending.AttemptCount);
        Assert.True(pending.NextAttemptAt > fixture.Time.GetUtcNow().UtcDateTime);

        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        var second = await service.ProcessBatchAsync(CancellationToken.None);
        var failed = await fixture.ReadSingleAsync();
        Assert.Equal(1, second.Failed);
        Assert.Equal("FAILED", failed.Status);
        Assert.Equal(2, failed.AttemptCount);
        Assert.Contains("broker unavailable", failed.LastError);
    }

    [Fact]
    public async Task StaleProcessingLeaseIsRecovered()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(status: "PROCESSING", lockedUntil: fixtureNow().AddSeconds(-1));
        var publisher = new RecordingPublisher();
        var service = fixture.CreateService(publisher);

        var result = await service.ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(1, result.Published);
        Assert.Single(publisher.Messages);
        Assert.Equal("PUBLISHED", (await fixture.ReadSingleAsync()).Status);

        static DateTime fixtureNow() => new(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);
    }

    [Fact]
    public async Task PublishedMessageIsNeverClaimedAgain()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedAsync(status: "PUBLISHED");
        var publisher = new RecordingPublisher();

        var result = await fixture.CreateService(publisher).ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(0, result.Claimed);
        Assert.Empty(publisher.Messages);
    }

    private sealed class Fixture(
        string path,
        SqliteContextFactory contextFactory,
        MutableTimeProvider time,
        RabbitMqOptions options) : IAsyncDisposable
    {
        public MutableTimeProvider Time { get; } = time;

        public static async Task<Fixture> CreateAsync(int maxAttempts = 8)
        {
            var path = Path.Combine(Path.GetTempPath(), $"showtime-outbox-{Guid.NewGuid():N}.db");
            var factory = new SqliteContextFactory($"Data Source={path};Pooling=False;Foreign Keys=False");
            await using var db = await factory.CreateDbContextAsync();
            await db.Database.EnsureCreatedAsync();
            return new Fixture(
                path,
                factory,
                new MutableTimeProvider(new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero)),
                new RabbitMqOptions { MaxPublishAttempts = maxAttempts });
        }

        public async Task SeedAsync(
            string status = "PENDING",
            DateTime? lockedUntil = null)
        {
            await using var db = await contextFactory.CreateDbContextAsync();
            var now = Time.GetUtcNow().UtcDateTime;
            db.OrderEventOutbox.Add(new OrderEventOutbox
            {
                EventId = "42ef4e11-af25-4ca8-9e0b-184b45bb8c65",
                EventType = OrderCreatedEvent.TypeName,
                RoutingKey = OrderCreatedEvent.RoutingKeyName,
                AggregateId = 101,
                UserId = 7,
                Payload = "{}",
                OccurredAt = now,
                Status = status,
                AttemptCount = 0,
                NextAttemptAt = now,
                LockedUntil = lockedUntil,
                LockOwner = status == "PROCESSING" ? "dead-worker" : null,
                CreateTime = now,
                UpdateTime = now,
            });
            await db.SaveChangesAsync();
        }

        public IOrderEventOutboxService CreateService(IOrderEventPublisher publisher) =>
            new OrderEventOutboxService(
                contextFactory,
                publisher,
                Time,
                Options.Create(options),
                NullLogger<OrderEventOutboxService>.Instance);

        public async Task<OrderEventOutbox> ReadSingleAsync()
        {
            await using var db = await contextFactory.CreateDbContextAsync();
            return await db.OrderEventOutbox.AsNoTracking().SingleAsync();
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class SqliteContextFactory(string connectionString) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new SqliteAuthDbContext(
            new DbContextOptionsBuilder<SqliteAuthDbContext>()
                .UseSqlite(connectionString)
                .Options);

        public Task<AppDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset current = utcNow;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan duration) => current += duration;
    }

    private sealed class RecordingPublisher : IOrderEventPublisher
    {
        public List<OrderEventOutbox> Messages { get; } = [];
        public Task PublishAsync(OrderEventOutbox message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingPublisher : IOrderEventPublisher
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task PublishAsync(OrderEventOutbox message, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ThrowingPublisher : IOrderEventPublisher
    {
        public Task PublishAsync(OrderEventOutbox message, CancellationToken cancellationToken) =>
            Task.FromException(new IOException("broker unavailable"));
    }

    private sealed class RecordingOutboxService : IOrderEventOutboxService
    {
        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount { get; private set; }

        public Task<OutboxBatchResult> ProcessBatchAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            Called.TrySetResult();
            return Task.FromResult(new OutboxBatchResult(0, 0, 0, 0));
        }
    }
}

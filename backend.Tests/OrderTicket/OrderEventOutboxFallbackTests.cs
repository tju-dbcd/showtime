using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Services.OrderTicket;
using ShowtimeBackend.Services.OrderTicket.Messaging;

namespace ShowtimeBackend.Tests.OrderTicket;

// 默认配置（RabbitMq:Enabled=false）下的回退链路测试：
// outbox worker + LocalOrderEventPublisher 在进程内完成退款与实时通知，
// 保证不启用 RabbitMQ 时批准退款不会永远停在 PROCESSING。
public sealed class OrderEventOutboxFallbackTests
{
    [Fact]
    public async Task LocalPublisher_CompletesApprovedRefundAndPushesStatusWithoutRabbitMq()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        await MakeFinancialsConsistentAsync(fixture);
        await fixture.ApproveAndReadEventAsync();

        var dispatcher = new RecordingDispatcher();
        await using var provider = CreateProvider(fixture, dispatcher);

        using (var scope = provider.CreateScope())
        {
            // 第一轮：处理 RefundApprovedEvent → 进程内完成退款 → 推送 PROCESSING
            var first = await scope.ServiceProvider
                .GetRequiredService<IOrderEventOutboxService>()
                .ProcessBatchAsync(CancellationToken.None);
            Assert.Equal(1, first.Published);
        }

        Assert.Equal("COMPLETED", await RefundStatusAsync(fixture));
        Assert.Equal("REFUNDED", await fixture.ItemStatusAsync());
        Assert.Equal("REFUNDED", await fixture.TicketStatusAsync());
        Assert.Equal("RELEASED", await fixture.ReservationStatusAsync());
        Assert.Equal(84m, await fixture.PaymentRefundAmountAsync());

        using (var scope = provider.CreateScope())
        {
            // 第二轮：处理完成事务写入的 RefundStatusChanged(COMPLETED) 通知
            var second = await scope.ServiceProvider
                .GetRequiredService<IOrderEventOutboxService>()
                .ProcessBatchAsync(CancellationToken.None);
            Assert.Equal(1, second.Published);
        }

        Assert.Equal(
            ["APPROVED/PROCESSING", "APPROVED/COMPLETED"],
            dispatcher.RefundStatusEvents
                .Select(item => $"{item.ApproveStatus}/{item.RefundStatus}")
                .ToArray());

        var rows = await fixture.Db.Set<OrderEventOutbox>().AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Equal("PUBLISHED", row.Status));
        Assert.All(rows, row => Assert.Null(row.LastError));
    }

    [Fact]
    public async Task LocalPublisher_DispatchesOrderCreatedNotificationWithoutRabbitMq()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        var notification = new OrderCreatedEvent(
            Guid.NewGuid().ToString("D"),
            OrderCreatedEvent.TypeName,
            DateTime.UtcNow,
            101,
            "ORD101",
            7,
            10,
            376m,
            2,
            "PENDING_PAY");
        await SeedOutboxRowAsync(fixture, notification.Serialize());

        var dispatcher = new RecordingDispatcher();
        await using var provider = CreateProvider(fixture, dispatcher);
        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<IOrderEventOutboxService>()
            .ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(1, result.Published);
        Assert.Equal(notification.EventId, dispatcher.Notification!.EventId);

        var row = await fixture.Db.Set<OrderEventOutbox>().AsNoTracking().SingleAsync();
        Assert.Equal("PUBLISHED", row.Status);
    }

    [Fact]
    public async Task LocalPublisher_HandlerRetryLeavesOutboxPendingForBackoffRetry()
    {
        await using var fixture = await RefundTestData.CreatePendingRefundAsync();
        await SeedOutboxRowAsync(
            fixture,
            new RefundStatusChangedEvent(
                Guid.NewGuid().ToString("D"),
                RefundStatusChangedEvent.TypeName,
                DateTime.UtcNow,
                401,
                "REF000401",
                101,
                7,
                "APPROVED",
                "COMPLETED",
                84m).Serialize());

        await using var provider = CreateProvider(
            fixture,
            new RecordingDispatcher(),
            new FixedOutcomeHandler(OrderNotificationHandlingResult.Retry));
        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<IOrderEventOutboxService>()
            .ProcessBatchAsync(CancellationToken.None);

        Assert.Equal(1, result.Claimed);
        Assert.Equal(0, result.Published);
        Assert.Equal(1, result.Retried);

        var persisted = await fixture.Db.Set<OrderEventOutbox>().AsNoTracking().SingleAsync();
        Assert.Equal("PENDING", persisted.Status);
        Assert.Equal(1, persisted.AttemptCount);
        Assert.True(persisted.NextAttemptAt > persisted.OccurredAt);
    }

    private static async Task SeedOutboxRowAsync(
        RefundTestData fixture,
        string payload)
    {
        fixture.Db.OrderEventOutbox.Add(new OrderEventOutbox
        {
            EventId = Guid.NewGuid().ToString("D"),
            EventType = OrderCreatedEvent.TypeName,
            RoutingKey = OrderCreatedEvent.RoutingKeyName,
            AggregateId = 101,
            UserId = 7,
            Payload = payload,
            OccurredAt = RefundTestData.FixedUtcNow,
            Status = "PENDING",
            AttemptCount = 0,
            NextAttemptAt = RefundTestData.FixedUtcNow,
            CreateBy = "tests",
            UpdateBy = "tests",
        });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
    }

    private static ServiceProvider CreateProvider(
        RefundTestData fixture,
        RecordingDispatcher dispatcher,
        IOrderNotificationMessageHandler? handler = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IOrderNotificationDispatcher>(_ => dispatcher);
        services.AddScoped<IRefundCompletionService>(_ => fixture.CreateCompletionService());
        services.AddScoped<IOrderNotificationMessageHandler>(_ =>
            handler ?? new OrderNotificationMessageHandler(
                dispatcher,
                fixture.CreateCompletionService(),
                NullLogger<OrderNotificationMessageHandler>.Instance));
        services.AddSingleton<IOrderEventPublisher, LocalOrderEventPublisher>();
        services.AddScoped<IOrderEventOutboxService>(sp =>
            new OrderEventOutboxService(
                new RefundTestDataDbContextFactory(fixture),
                sp.GetRequiredService<IOrderEventPublisher>(),
                fixture.TimeProvider,
                Options.Create(new RabbitMqOptions()),
                NullLogger<OrderEventOutboxService>.Instance));
        return services.BuildServiceProvider();
    }

    private static async Task MakeFinancialsConsistentAsync(RefundTestData fixture)
    {
        var total = await fixture.Db.Set<OrderItem>().SumAsync(item => item.UnitPrice);
        var order = await fixture.Db.Set<Order>().SingleAsync();
        var payment = await fixture.Db.Set<Payment>().SingleAsync();
        order.TotalAmount = total;
        order.DiscountAmount = 0m;
        payment.PayAmount = total;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
    }

    private static Task<string> RefundStatusAsync(RefundTestData fixture) =>
        fixture.Db.Set<RefundRequest>()
            .AsNoTracking()
            .Where(item => item.RefundId == fixture.RefundId)
            .Select(item => item.RefundStatus)
            .SingleAsync();

    private sealed class RefundTestDataDbContextFactory(
        RefundTestData fixture) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => fixture.CreateDbContext();

        public Task<AppDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class FixedOutcomeHandler(
        OrderNotificationHandlingResult outcome) : IOrderNotificationMessageHandler
    {
        public Task<OrderNotificationHandlingResult> HandleAsync(
            string? messageType,
            ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken) =>
            Task.FromResult(outcome);
    }

    private sealed class RecordingDispatcher : IOrderNotificationDispatcher
    {
        public OrderCreatedEvent? Notification { get; private set; }
        public List<RefundStatusChangedEvent> RefundStatusEvents { get; } = [];

        public Task DispatchOrderCreatedAsync(
            OrderCreatedEvent notification,
            CancellationToken cancellationToken)
        {
            Notification = notification;
            return Task.CompletedTask;
        }

        public Task DispatchRefundStatusChangedAsync(
            RefundStatusChangedEvent statusEvent,
            CancellationToken cancellationToken)
        {
            RefundStatusEvents.Add(statusEvent);
            return Task.CompletedTask;
        }
    }
}

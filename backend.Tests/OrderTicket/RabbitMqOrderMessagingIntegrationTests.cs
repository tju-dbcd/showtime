using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Services.OrderTicket.Messaging;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class RabbitMqOrderMessagingIntegrationTests
{
    [RabbitMqFact]
    public async Task ProductionTopologyAndPublisherConfirmPersistentMandatoryRouting()
    {
        await using var fixture = await Fixture.CreateAsync();
        var message = fixture.CreateMessage();

        await fixture.Publisher.PublishAsync(message, CancellationToken.None);
        var delivery = await fixture.WaitForMessageAsync(fixture.Options.OrderNotificationQueueName);

        Assert.True(delivery.BasicProperties.Persistent);
        Assert.Equal("application/json", delivery.BasicProperties.ContentType);
        Assert.Equal(message.EventId, delivery.BasicProperties.MessageId);
        Assert.Equal(OrderCreatedEvent.TypeName, delivery.BasicProperties.Type);
        await fixture.Channel.BasicAckAsync(delivery.DeliveryTag, false);

        await fixture.Channel.QueueUnbindAsync(
            fixture.Options.OrderNotificationQueueName,
            fixture.Options.ExchangeName,
            OrderCreatedEvent.RoutingKeyName);
        await Assert.ThrowsAnyAsync<PublishException>(() =>
            fixture.Publisher.PublishAsync(fixture.CreateMessage(), CancellationToken.None));
        await fixture.Channel.QueueBindAsync(
            fixture.Options.OrderNotificationQueueName,
            fixture.Options.ExchangeName,
            OrderCreatedEvent.RoutingKeyName);
    }

    [RabbitMqFact]
    public async Task ProductionConsumerDispatchesAndAcknowledgesSuccessfulNotification()
    {
        await using var fixture = await Fixture.CreateAsync();
        var dispatcher = new TestDispatcher();
        var refundCompletion = new TestRefundCompletionService();
        await using var consumer = await fixture.StartConsumerAsync(
            dispatcher,
            refundCompletion);
        await fixture.Publisher.PublishAsync(fixture.CreateMessage(), CancellationToken.None);
        await fixture.Publisher.PublishAsync(
            fixture.CreateRefundMessage(),
            CancellationToken.None);

        await dispatcher.Called.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await refundCompletion.Called.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await dispatcher.RefundStatusCalled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(200);
        await consumer.StopAsync();

        Assert.Equal(1, dispatcher.OrderCreatedCallCount);
        Assert.Equal(1, dispatcher.RefundStatusCallCount);
        Assert.Equal(1, refundCompletion.CallCount);
        Assert.Null(await fixture.Channel.BasicGetAsync(
            fixture.Options.OrderNotificationQueueName,
            autoAck: true));
    }

    [RabbitMqFact]
    public async Task PersistentDispatcherFailureRetriesToBoundThenDeadLetters()
    {
        await using var fixture = await Fixture.CreateAsync(consumerMaxRetries: 2);
        var dispatcher = new TestDispatcher { Failure = new IOException("SignalR unavailable") };
        await using var consumer = await fixture.StartConsumerAsync(dispatcher);
        await fixture.Publisher.PublishAsync(fixture.CreateMessage(), CancellationToken.None);

        var deadLetter = await fixture.WaitForMessageAsync(fixture.Options.DeadLetterQueueName);
        Assert.True(OrderNotificationDeliveryProcessor.TryReadRetryCount(
            deadLetter.BasicProperties.Headers,
            out var retryCount));
        Assert.Equal(2, retryCount);
        Assert.Equal(3, dispatcher.OrderCreatedCallCount);
        await fixture.Channel.BasicAckAsync(deadLetter.DeliveryTag, false);
        await consumer.StopAsync();

        Assert.Null(await fixture.Channel.BasicGetAsync(
            fixture.Options.OrderNotificationQueueName,
            autoAck: true));
    }

    [RabbitMqFact]
    public async Task MalformedAndUnknownEventsUseProductionDeadLetterTopology()
    {
        await using var fixture = await Fixture.CreateAsync();
        var dispatcher = new TestDispatcher();
        await using var consumer = await fixture.StartConsumerAsync(dispatcher);
        var malformed = fixture.CreateMessage(payload: "not-json");
        var unknown = fixture.CreateMessage(eventType: "OrderUnknown.v1");

        await fixture.Publisher.PublishAsync(malformed, CancellationToken.None);
        await fixture.Publisher.PublishAsync(unknown, CancellationToken.None);
        var first = await fixture.WaitForMessageAsync(fixture.Options.DeadLetterQueueName);
        await fixture.Channel.BasicAckAsync(first.DeliveryTag, false);
        var second = await fixture.WaitForMessageAsync(fixture.Options.DeadLetterQueueName);
        await fixture.Channel.BasicAckAsync(second.DeliveryTag, false);
        await consumer.StopAsync();

        Assert.Equal(0, dispatcher.OrderCreatedCallCount);
        Assert.Equal(0, dispatcher.RefundStatusCallCount);
        Assert.Null(await fixture.Channel.BasicGetAsync(
            fixture.Options.OrderNotificationQueueName,
            autoAck: true));
    }

    [RabbitMqFact]
    public async Task ConsumerCancellationWakesLoopAndRebuildsChannelAndTopology()
    {
        await using var fixture = await Fixture.CreateAsync();
        var dispatcher = new TestDispatcher();
        await using var consumer = await fixture.StartConsumerAsync(dispatcher);

        await fixture.Channel.QueueDeleteAsync(
            fixture.Options.OrderNotificationQueueName,
            ifUnused: false,
            ifEmpty: false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            try
            {
                await fixture.Publisher.PublishAsync(
                    fixture.CreateMessage(),
                    timeout.Token);
                break;
            }
            catch (PublishException)
            {
                await Task.Delay(100, timeout.Token);
            }
        }

        await dispatcher.Called.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await consumer.StopAsync();
        Assert.Equal(1, dispatcher.OrderCreatedCallCount);
    }

    private sealed class Fixture(
        RabbitMqConnectionProvider connectionProvider,
        RabbitMqOrderEventPublisher publisher,
        IChannel channel,
        RabbitMqOptions options) : IAsyncDisposable
    {
        public RabbitMqOrderEventPublisher Publisher { get; } = publisher;
        public IChannel Channel { get; } = channel;
        public RabbitMqOptions Options { get; } = options;

        public static async Task<Fixture> CreateAsync(int consumerMaxRetries = 3)
        {
            var raw = Environment.GetEnvironmentVariable("SHOWTIME_RABBITMQ_TEST_CONNECTION");
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new InvalidOperationException(
                    "SHOWTIME_RUN_RABBITMQ_TESTS=1 requires SHOWTIME_RABBITMQ_TEST_CONNECTION.");
            }

            var suffix = Guid.NewGuid().ToString("N");
            var options = new RabbitMqOptions
            {
                Enabled = true,
                ExchangeName = $"showtime.tests.order.events.{suffix}",
                OrderNotificationQueueName = $"showtime.tests.order.notifications.{suffix}",
                DeadLetterExchangeName = $"showtime.tests.order.dlx.{suffix}",
                DeadLetterQueueName = $"showtime.tests.order.dlq.{suffix}",
                ConsumerMaxRetries = consumerMaxRetries,
                OutboxPollIntervalSeconds = 1,
            };
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:RabbitMq"] = raw,
                })
                .Build();
            var provider = new RabbitMqConnectionProvider(configuration);
            var optionValue = Microsoft.Extensions.Options.Options.Create(options);
            var publisher = new RabbitMqOrderEventPublisher(provider, optionValue);
            var connection = await provider.GetConnectionAsync(CancellationToken.None);
            var channel = await connection.CreateChannelAsync();
            await RabbitMqTopology.DeclareAsync(channel, options, CancellationToken.None);
            return new Fixture(provider, publisher, channel, options);
        }

        public OrderEventOutbox CreateMessage(
            string? payload = null,
            string eventType = OrderCreatedEvent.TypeName)
        {
            var eventId = Guid.NewGuid().ToString("D");
            var notification = new OrderCreatedEvent(
                eventId,
                OrderCreatedEvent.TypeName,
                DateTime.UtcNow,
                101,
                "ORD101",
                7,
                10,
                100m,
                1,
                "PENDING_PAY");
            return new OrderEventOutbox
            {
                EventId = eventId,
                EventType = eventType,
                RoutingKey = OrderCreatedEvent.RoutingKeyName,
                AggregateId = 101,
                UserId = 7,
                Payload = payload ?? notification.Serialize(),
                OccurredAt = notification.OccurredAt,
                NextAttemptAt = notification.OccurredAt,
            };
        }

        public OrderEventOutbox CreateRefundMessage()
        {
            var approvedEvent = new RefundApprovedEvent(
                Guid.NewGuid().ToString("D"),
                RefundApprovedEvent.TypeName,
                DateTime.UtcNow,
                401,
                "REF000401",
                101,
                7,
                84m);
            return new OrderEventOutbox
            {
                EventId = approvedEvent.EventId,
                EventType = approvedEvent.EventType,
                RoutingKey = RefundApprovedEvent.RoutingKeyName,
                AggregateId = approvedEvent.RefundId,
                UserId = approvedEvent.UserId,
                Payload = approvedEvent.Serialize(),
                OccurredAt = approvedEvent.OccurredAt,
                NextAttemptAt = approvedEvent.OccurredAt,
            };
        }

        public async Task<ConsumerHandle> StartConsumerAsync(
            TestDispatcher dispatcher,
            TestRefundCompletionService? refundCompletion = null)
        {
            refundCompletion ??= new TestRefundCompletionService();
            var services = new ServiceCollection()
                .AddLogging()
                .AddSingleton<IOrderNotificationDispatcher>(dispatcher)
                .AddSingleton<IRefundCompletionService>(refundCompletion)
                .AddScoped<IOrderNotificationMessageHandler, OrderNotificationMessageHandler>()
                .AddScoped<OrderNotificationDeliveryProcessor>()
                .AddSingleton<IOptions<RabbitMqOptions>>(
                    Microsoft.Extensions.Options.Options.Create(Options))
                .BuildServiceProvider();
            var worker = new RabbitMqOrderNotificationWorker(
                connectionProvider,
                services.GetRequiredService<IServiceScopeFactory>(),
                Microsoft.Extensions.Options.Options.Create(Options),
                NullLogger<RabbitMqOrderNotificationWorker>.Instance);
            await worker.StartAsync(CancellationToken.None);
            await WaitForConsumerAsync();
            return new ConsumerHandle(worker, services);
        }

        public async Task<BasicGetResult> WaitForMessageAsync(string queue)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                var delivery = await Channel.BasicGetAsync(queue, autoAck: false, timeout.Token);
                if (delivery is not null)
                {
                    return delivery;
                }
                await Task.Delay(50, timeout.Token);
            }
        }

        private async Task WaitForConsumerAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                var state = await Channel.QueueDeclarePassiveAsync(
                    Options.OrderNotificationQueueName,
                    timeout.Token);
                if (state.ConsumerCount > 0)
                {
                    return;
                }
                await Task.Delay(50, timeout.Token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await TryCleanupAsync(channel => channel.QueueDeleteAsync(
                    Options.OrderNotificationQueueName, false, false));
                await TryCleanupAsync(channel => channel.QueueDeleteAsync(
                    Options.DeadLetterQueueName, false, false));
                await TryCleanupAsync(channel => channel.ExchangeDeleteAsync(
                    Options.ExchangeName, false));
                await TryCleanupAsync(channel => channel.ExchangeDeleteAsync(
                    Options.DeadLetterExchangeName, false));
            }
            finally
            {
                await Channel.DisposeAsync();
                await Publisher.DisposeAsync();
                await connectionProvider.DisposeAsync();
            }
        }

        private async Task TryCleanupAsync(Func<IChannel, Task> cleanup)
        {
            try
            {
                var connection = await connectionProvider.GetConnectionAsync(CancellationToken.None);
                await using var cleanupChannel = await connection.CreateChannelAsync();
                await cleanup(cleanupChannel);
            }
            catch (Exception)
            {
                // Each test owns globally unique names. Cleanup is best-effort per object so
                // one already-absent object cannot prevent removal of the remaining topology.
            }
        }
    }

    private sealed class ConsumerHandle(
        RabbitMqOrderNotificationWorker worker,
        ServiceProvider services) : IAsyncDisposable
    {
        private bool stopped;

        public async Task StopAsync()
        {
            if (stopped)
            {
                return;
            }
            stopped = true;
            await worker.StopAsync(CancellationToken.None);
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
            worker.Dispose();
            await services.DisposeAsync();
        }
    }

    private sealed class TestDispatcher : IOrderNotificationDispatcher
    {
        private int orderCreatedCallCount;
        private int refundStatusCallCount;
        public int OrderCreatedCallCount => Volatile.Read(ref orderCreatedCallCount);
        public int RefundStatusCallCount => Volatile.Read(ref refundStatusCallCount);
        public Exception? Failure { get; init; }
        public TaskCompletionSource Called { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RefundStatusCalled { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DispatchOrderCreatedAsync(
            OrderCreatedEvent notification,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref orderCreatedCallCount);
            Called.TrySetResult();
            return Failure is null
                ? Task.CompletedTask
                : Task.FromException(Failure);
        }

        public Task DispatchRefundStatusChangedAsync(
            RefundStatusChangedEvent statusEvent,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref refundStatusCallCount);
            RefundStatusCalled.TrySetResult();
            return Failure is null
                ? Task.CompletedTask
                : Task.FromException(Failure);
        }
    }

    private sealed class TestRefundCompletionService : IRefundCompletionService
    {
        private int callCount;
        public int CallCount => Volatile.Read(ref callCount);
        public TaskCompletionSource Called { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<RefundCompletionResult> CompleteAsync(
            RefundApprovedEvent approvedEvent,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref callCount);
            Called.TrySetResult();
            return Task.FromResult(RefundCompletionResult.Completed());
        }
    }

    private sealed class RabbitMqFactAttribute : FactAttribute
    {
        public RabbitMqFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("SHOWTIME_RUN_RABBITMQ_TESTS") != "1")
            {
                Skip = "SHOWTIME_RUN_RABBITMQ_TESTS is not 1; no RabbitMQ connection will be opened.";
            }
        }
    }
}

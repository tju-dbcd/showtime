using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShowtimeBackend.Services.OrderTicket.Messaging;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class OrderNotificationDeliveryProcessorTests
{
    [Fact]
    public async Task SuccessfulDispatchAcknowledgesDelivery()
    {
        var delivery = new FakeDelivery();
        var outcome = await CreateProcessor(OrderNotificationHandlingResult.Acknowledge)
            .ProcessAsync(delivery, CancellationToken.None);

        Assert.Equal(OrderNotificationDeliveryOutcome.Completed, outcome);
        Assert.Equal(["ack"], delivery.Actions);
    }

    [Fact]
    public async Task RetryIsAcknowledgedOnlyAfterConfirmedCopy()
    {
        var delivery = new FakeDelivery();
        var outcome = await CreateProcessor(OrderNotificationHandlingResult.Retry)
            .ProcessAsync(delivery, CancellationToken.None);

        Assert.Equal(OrderNotificationDeliveryOutcome.Completed, outcome);
        Assert.Equal(["publish:1", "ack"], delivery.Actions);
    }

    [Fact]
    public async Task RetryPublishFailureDeadLettersOriginalWithoutRequeueLoop()
    {
        var delivery = new FakeDelivery { PublishFailure = new IOException("publisher nack") };
        var outcome = await CreateProcessor(OrderNotificationHandlingResult.Retry)
            .ProcessAsync(delivery, CancellationToken.None);

        Assert.Equal(OrderNotificationDeliveryOutcome.Completed, outcome);
        Assert.Equal(["publish:1", "dead-letter"], delivery.Actions);
        Assert.DoesNotContain("ack", delivery.Actions);
    }

    [Fact]
    public async Task RetryPublishFailureThatClosesChannelWakesConsumerRebuildPath()
    {
        var delivery = new FakeDelivery
        {
            PublishFailure = new IOException("channel closed"),
            CloseChannelOnPublishFailure = true,
        };
        var outcome = await CreateProcessor(OrderNotificationHandlingResult.Retry)
            .ProcessAsync(delivery, CancellationToken.None);

        Assert.Equal(OrderNotificationDeliveryOutcome.ChannelUnavailable, outcome);
        Assert.Equal(["publish:1"], delivery.Actions);
    }

    [Fact]
    public async Task RetryAtLimitGoesDirectlyToDeadLetterQueue()
    {
        var delivery = new FakeDelivery
        {
            Headers = new Dictionary<string, object?>
            {
                [OrderNotificationDeliveryProcessor.RetryHeader] = 2,
            },
        };
        var outcome = await CreateProcessor(
            OrderNotificationHandlingResult.Retry,
            maximumRetries: 2).ProcessAsync(delivery, CancellationToken.None);

        Assert.Equal(OrderNotificationDeliveryOutcome.Completed, outcome);
        Assert.Equal(["dead-letter"], delivery.Actions);
    }

    [Theory]
    [MemberData(nameof(InvalidRetryHeaders))]
    public async Task InvalidRetryHeaderIsPoisonAndCannotResetAttempts(object invalidValue)
    {
        var handler = new FakeHandler(OrderNotificationHandlingResult.Acknowledge);
        var delivery = new FakeDelivery
        {
            Headers = new Dictionary<string, object?>
            {
                [OrderNotificationDeliveryProcessor.RetryHeader] = invalidValue,
            },
        };
        var outcome = await CreateProcessor(handler).ProcessAsync(delivery, CancellationToken.None);

        Assert.Equal(OrderNotificationDeliveryOutcome.Completed, outcome);
        Assert.Equal(["dead-letter"], delivery.Actions);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task UnexpectedHandlerFailureIsContainedAndDeadLettered()
    {
        var delivery = new FakeDelivery();
        var outcome = await CreateProcessor(new FakeHandler(
            OrderNotificationHandlingResult.Acknowledge,
            new InvalidOperationException("scope dependency failed")))
            .ProcessAsync(delivery, CancellationToken.None);

        Assert.Equal(OrderNotificationDeliveryOutcome.Completed, outcome);
        Assert.Equal(["dead-letter"], delivery.Actions);
    }

    [Fact]
    public async Task ClosedChannelReturnsObservableUnavailableOutcome()
    {
        var delivery = new FakeDelivery { IsChannelOpen = false };
        var outcome = await CreateProcessor(OrderNotificationHandlingResult.DeadLetter)
            .ProcessAsync(delivery, CancellationToken.None);

        Assert.Equal(OrderNotificationDeliveryOutcome.ChannelUnavailable, outcome);
        Assert.Empty(delivery.Actions);
    }

    [Fact]
    public async Task ConsumerLifetimeShutdownSignalWakesWaiter()
    {
        var lifetime = new RabbitMqConsumerLifetime();
        var waiting = lifetime.WaitAsync(CancellationToken.None);

        lifetime.SignalEnded();

        await waiting.WaitAsync(TimeSpan.FromSeconds(1));
    }

    public static TheoryData<object> InvalidRetryHeaders => new()
    {
        -1,
        (long)int.MaxValue + 1,
        "3",
        new byte[] { 3 },
    };

    private static OrderNotificationDeliveryProcessor CreateProcessor(
        OrderNotificationHandlingResult result,
        int maximumRetries = 3) =>
        CreateProcessor(new FakeHandler(result), maximumRetries);

    private static OrderNotificationDeliveryProcessor CreateProcessor(
        IOrderNotificationMessageHandler handler,
        int maximumRetries = 3) => new(
            handler,
            Options.Create(new RabbitMqOptions { ConsumerMaxRetries = maximumRetries }),
            NullLogger<OrderNotificationDeliveryProcessor>.Instance);

    private sealed class FakeHandler(
        OrderNotificationHandlingResult result,
        Exception? failure = null) : IOrderNotificationMessageHandler
    {
        public int CallCount { get; private set; }

        public Task<OrderNotificationHandlingResult> HandleAsync(
            string? messageType,
            ReadOnlyMemory<byte> body,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return failure is null
                ? Task.FromResult(result)
                : Task.FromException<OrderNotificationHandlingResult>(failure);
        }
    }

    private sealed class FakeDelivery : IOrderNotificationDelivery
    {
        public string? MessageType { get; init; } = OrderCreatedEvent.TypeName;
        public ReadOnlyMemory<byte> Body { get; init; } = "{}"u8.ToArray();
        public IDictionary<string, object?>? Headers { get; init; }
        public bool IsChannelOpen { get; set; } = true;
        public Exception? PublishFailure { get; init; }
        public bool CloseChannelOnPublishFailure { get; init; }
        public List<string> Actions { get; } = [];

        public Task AcknowledgeAsync(CancellationToken cancellationToken)
        {
            Actions.Add("ack");
            return Task.CompletedTask;
        }

        public Task DeadLetterAsync(CancellationToken cancellationToken)
        {
            Actions.Add("dead-letter");
            return Task.CompletedTask;
        }

        public Task PublishRetryAsync(int retryCount, CancellationToken cancellationToken)
        {
            Actions.Add($"publish:{retryCount}");
            if (PublishFailure is not null && CloseChannelOnPublishFailure)
            {
                IsChannelOpen = false;
            }
            return PublishFailure is null
                ? Task.CompletedTask
                : Task.FromException(PublishFailure);
        }
    }
}

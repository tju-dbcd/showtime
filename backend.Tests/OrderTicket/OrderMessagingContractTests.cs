using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Services.OrderTicket.Messaging;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class OrderMessagingContractTests
{
    [Fact]
    public void RabbitMqOptions_DefaultsAreStableAndValid()
    {
        var options = new RabbitMqOptions();
        var results = new List<ValidationResult>();

        Assert.True(Validator.TryValidateObject(options, new ValidationContext(options), results, true));
        Assert.False(options.Enabled);
        Assert.Equal("showtime.order-ticket.events", options.ExchangeName);
        Assert.Equal("showtime.order.notifications.v1", options.OrderNotificationQueueName);
        Assert.Equal((ushort)16, options.PrefetchCount);
        Assert.Equal(8, options.MaxPublishAttempts);
    }

    [Fact]
    public void RabbitMqOptions_InvalidRangesFailDataAnnotationValidation()
    {
        var options = new RabbitMqOptions
        {
            PublishBatchSize = 0,
            PrefetchCount = 0,
            MaxPublishAttempts = 0,
        };
        var results = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(options, new ValidationContext(options), results, true));
        Assert.Equal(3, results.Count);
    }

    [Fact]
    public void RabbitMqOptions_EnabledWithoutConnectionFailsValidation()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();
        var result = new RabbitMqOptionsValidator(configuration)
            .Validate(null, new RabbitMqOptions { Enabled = true });

        Assert.True(result.Failed);
        Assert.Contains("ConnectionStrings:RabbitMq", result.FailureMessage);
    }

    [Fact]
    public void OrderCreatedEvent_SerializesStableCamelCaseContract()
    {
        var notification = new OrderCreatedEvent(
            "42ef4e11-af25-4ca8-9e0b-184b45bb8c65",
            OrderCreatedEvent.TypeName,
            new DateTime(2026, 9, 5, 2, 3, 4, DateTimeKind.Utc),
            101,
            "ORD101",
            7,
            10,
            376m,
            2,
            "PENDING_PAY");

        var json = notification.Serialize();

        Assert.Equal(
            "{\"eventId\":\"42ef4e11-af25-4ca8-9e0b-184b45bb8c65\",\"eventType\":\"OrderCreated.v1\",\"occurredAt\":\"2026-09-05T02:03:04Z\",\"orderId\":101,\"orderNo\":\"ORD101\",\"userId\":7,\"sessionId\":10,\"totalAmount\":376,\"ticketCount\":2,\"orderStatus\":\"PENDING_PAY\"}",
            json);
    }

    [Fact]
    public async Task MessageHandler_AcknowledgesOnlyAfterSuccessfulDispatch()
    {
        var dispatcher = new RecordingDispatcher();
        var handler = new OrderNotificationMessageHandler(
            dispatcher,
            new StubRefundCompletionService(),
            NullLogger<OrderNotificationMessageHandler>.Instance);
        var notification = CreateNotification();

        var result = await handler.HandleAsync(
            OrderCreatedEvent.TypeName,
            Encoding.UTF8.GetBytes(notification.Serialize()),
            CancellationToken.None);

        Assert.Equal(OrderNotificationHandlingResult.Acknowledge, result);
        Assert.Equal(notification.EventId, dispatcher.Notification!.EventId);
    }

    [Theory]
    [InlineData("Unknown.v1", "{}")]
    [InlineData("OrderCreated.v1", "not-json")]
    [InlineData("OrderCreated.v1", "{}")]
    public async Task MessageHandler_DeadLettersUnknownOrMalformedMessages(string type, string json)
    {
        var handler = new OrderNotificationMessageHandler(
            new RecordingDispatcher(),
            new StubRefundCompletionService(),
            NullLogger<OrderNotificationMessageHandler>.Instance);

        var result = await handler.HandleAsync(type, Encoding.UTF8.GetBytes(json), CancellationToken.None);

        Assert.Equal(OrderNotificationHandlingResult.DeadLetter, result);
    }

    [Fact]
    public async Task MessageHandler_ReturnsRetryForTransientDispatcherFailure()
    {
        var handler = new OrderNotificationMessageHandler(
            new RecordingDispatcher { Failure = new IOException("transient") },
            new StubRefundCompletionService(),
            NullLogger<OrderNotificationMessageHandler>.Instance);

        var result = await handler.HandleAsync(
            OrderCreatedEvent.TypeName,
            Encoding.UTF8.GetBytes(CreateNotification().Serialize()),
            CancellationToken.None);

        Assert.Equal(OrderNotificationHandlingResult.Retry, result);
    }

    [Theory]
    [InlineData(RefundCompletionOutcome.Completed, OrderNotificationHandlingResult.Acknowledge)]
    [InlineData(RefundCompletionOutcome.AlreadyCompleted, OrderNotificationHandlingResult.Acknowledge)]
    [InlineData(RefundCompletionOutcome.RetryableFailure, OrderNotificationHandlingResult.Retry)]
    [InlineData(RefundCompletionOutcome.PermanentFailure, OrderNotificationHandlingResult.DeadLetter)]
    public async Task MessageHandler_MapsRefundCompletionOutcome(
        RefundCompletionOutcome completionOutcome,
        OrderNotificationHandlingResult expected)
    {
        var completion = new StubRefundCompletionService(completionOutcome);
        var handler = new OrderNotificationMessageHandler(
            new RecordingDispatcher(),
            completion,
            NullLogger<OrderNotificationMessageHandler>.Instance);
        var approvedEvent = CreateRefundApprovedEvent();

        var result = await handler.HandleAsync(
            RefundApprovedEvent.TypeName,
            Encoding.UTF8.GetBytes(approvedEvent.Serialize()),
            CancellationToken.None);

        Assert.Equal(expected, result);
        Assert.Equal(approvedEvent.EventId, completion.Received!.EventId);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    public async Task MessageHandler_DeadLettersMalformedOrInvalidRefundEvent(string json)
    {
        var completion = new StubRefundCompletionService(RefundCompletionOutcome.PermanentFailure);
        var handler = new OrderNotificationMessageHandler(
            new RecordingDispatcher(),
            completion,
            NullLogger<OrderNotificationMessageHandler>.Instance);

        var result = await handler.HandleAsync(
            RefundApprovedEvent.TypeName,
            Encoding.UTF8.GetBytes(json),
            CancellationToken.None);

        Assert.Equal(OrderNotificationHandlingResult.DeadLetter, result);
    }

    [Fact]
    public void RefundStatusChangedEvent_SerializesStableCamelCaseContract()
    {
        var statusEvent = new RefundStatusChangedEvent(
            "42ef4e11-af25-4ca8-9e0b-184b45bb8c65",
            RefundStatusChangedEvent.TypeName,
            new DateTime(2026, 9, 5, 2, 3, 4, DateTimeKind.Utc),
            401,
            "REF000401",
            101,
            7,
            "APPROVED",
            "COMPLETED",
            84m);

        var json = statusEvent.Serialize();

        Assert.Equal(
            "{\"eventId\":\"42ef4e11-af25-4ca8-9e0b-184b45bb8c65\",\"eventType\":\"RefundStatusChanged.v1\",\"occurredAt\":\"2026-09-05T02:03:04Z\",\"refundId\":401,\"refundNo\":\"REF000401\",\"orderId\":101,\"userId\":7,\"approveStatus\":\"APPROVED\",\"refundStatus\":\"COMPLETED\",\"actualRefund\":84}",
            json);
    }

    [Fact]
    public async Task MessageHandler_RefundApprovalDispatchesProcessingStatusBeforeCompletion()
    {
        var dispatcher = new RecordingDispatcher();
        var handler = new OrderNotificationMessageHandler(
            dispatcher,
            new StubRefundCompletionService(),
            NullLogger<OrderNotificationMessageHandler>.Instance);
        var approvedEvent = CreateRefundApprovedEvent();

        var result = await handler.HandleAsync(
            RefundApprovedEvent.TypeName,
            Encoding.UTF8.GetBytes(approvedEvent.Serialize()),
            CancellationToken.None);

        Assert.Equal(OrderNotificationHandlingResult.Acknowledge, result);
        Assert.NotNull(dispatcher.RefundStatus);
        Assert.Equal("APPROVED", dispatcher.RefundStatus!.ApproveStatus);
        Assert.Equal("PROCESSING", dispatcher.RefundStatus.RefundStatus);
    }

    [Fact]
    public async Task MessageHandler_DispatchesRefundStatusChangedAndAcknowledges()
    {
        var dispatcher = new RecordingDispatcher();
        var handler = new OrderNotificationMessageHandler(
            dispatcher,
            new StubRefundCompletionService(),
            NullLogger<OrderNotificationMessageHandler>.Instance);
        var statusEvent = CreateRefundStatusChangedEvent();

        var result = await handler.HandleAsync(
            RefundStatusChangedEvent.TypeName,
            Encoding.UTF8.GetBytes(statusEvent.Serialize()),
            CancellationToken.None);

        Assert.Equal(OrderNotificationHandlingResult.Acknowledge, result);
        Assert.Equal(statusEvent.EventId, dispatcher.RefundStatus!.EventId);
        Assert.Equal("COMPLETED", dispatcher.RefundStatus!.RefundStatus);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    public async Task MessageHandler_DeadLettersMalformedOrInvalidRefundStatusEvent(string json)
    {
        var handler = new OrderNotificationMessageHandler(
            new RecordingDispatcher(),
            new StubRefundCompletionService(),
            NullLogger<OrderNotificationMessageHandler>.Instance);

        var result = await handler.HandleAsync(
            RefundStatusChangedEvent.TypeName,
            Encoding.UTF8.GetBytes(json),
            CancellationToken.None);

        Assert.Equal(OrderNotificationHandlingResult.DeadLetter, result);
    }

    [Fact]
    public async Task MessageHandler_ReturnsRetryForTransientRefundStatusDispatchFailure()
    {
        var handler = new OrderNotificationMessageHandler(
            new RecordingDispatcher { Failure = new IOException("transient") },
            new StubRefundCompletionService(),
            NullLogger<OrderNotificationMessageHandler>.Instance);
        var statusEvent = CreateRefundStatusChangedEvent();

        var result = await handler.HandleAsync(
            RefundStatusChangedEvent.TypeName,
            Encoding.UTF8.GetBytes(statusEvent.Serialize()),
            CancellationToken.None);

        Assert.Equal(OrderNotificationHandlingResult.Retry, result);
    }

    [Fact]
    public void ConsumerRetryHeaderIsDurableAndBoundedInput()
    {
        Assert.True(OrderNotificationDeliveryProcessor.TryReadRetryCount(null, out var missing));
        Assert.Equal(0, missing);
        Assert.True(OrderNotificationDeliveryProcessor.TryReadRetryCount(
            new Dictionary<string, object?>
            {
                [OrderNotificationDeliveryProcessor.RetryHeader] = 3,
            },
            out var valid));
        Assert.Equal(3, valid);
        Assert.False(OrderNotificationDeliveryProcessor.TryReadRetryCount(
            new Dictionary<string, object?>
            {
                [OrderNotificationDeliveryProcessor.RetryHeader] = -1,
            },
            out _));
        Assert.False(OrderNotificationDeliveryProcessor.TryReadRetryCount(
            new Dictionary<string, object?>
            {
                [OrderNotificationDeliveryProcessor.RetryHeader] = (long)int.MaxValue + 1,
            },
            out _));
        Assert.False(OrderNotificationDeliveryProcessor.TryReadRetryCount(
            new Dictionary<string, object?>
            {
                [OrderNotificationDeliveryProcessor.RetryHeader] = "0",
            },
            out _));
    }

    private static OrderCreatedEvent CreateNotification() => new(
        Guid.NewGuid().ToString("D"),
        OrderCreatedEvent.TypeName,
        DateTime.UtcNow,
        101,
        "ORD101",
        7,
        10,
        100m,
        1,
        "PENDING_PAY");

    private static RefundApprovedEvent CreateRefundApprovedEvent() => new(
        Guid.NewGuid().ToString("D"),
        RefundApprovedEvent.TypeName,
        DateTime.UtcNow,
        401,
        "REF000401",
        101,
        7,
        84m);

    private static RefundStatusChangedEvent CreateRefundStatusChangedEvent() => new(
        Guid.NewGuid().ToString("D"),
        RefundStatusChangedEvent.TypeName,
        DateTime.UtcNow,
        401,
        "REF000401",
        101,
        7,
        "APPROVED",
        "COMPLETED",
        84m);

    private sealed class RecordingDispatcher : IOrderNotificationDispatcher
    {
        public OrderCreatedEvent? Notification { get; private set; }
        public RefundStatusChangedEvent? RefundStatus { get; private set; }
        public Exception? Failure { get; init; }

        public Task DispatchOrderCreatedAsync(OrderCreatedEvent notification, CancellationToken cancellationToken)
        {
            Notification = notification;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }

        public Task DispatchRefundStatusChangedAsync(
            RefundStatusChangedEvent statusEvent,
            CancellationToken cancellationToken)
        {
            RefundStatus = statusEvent;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class StubRefundCompletionService(
        RefundCompletionOutcome outcome = RefundCompletionOutcome.Completed)
        : IRefundCompletionService
    {
        public RefundApprovedEvent? Received { get; private set; }

        public Task<RefundCompletionResult> CompleteAsync(
            RefundApprovedEvent approvedEvent,
            CancellationToken cancellationToken)
        {
            Received = approvedEvent;
            var result = outcome switch
            {
                RefundCompletionOutcome.Completed => RefundCompletionResult.Completed(),
                RefundCompletionOutcome.AlreadyCompleted => RefundCompletionResult.AlreadyCompleted(),
                RefundCompletionOutcome.PermanentFailure => RefundCompletionResult.Permanent("test", "test"),
                _ => RefundCompletionResult.Retryable("test", "test"),
            };
            return Task.FromResult(result);
        }
    }
}

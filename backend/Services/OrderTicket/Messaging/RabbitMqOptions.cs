using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace ShowtimeBackend.Services.OrderTicket.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public bool Enabled { get; set; }
    [Required, StringLength(200)] public string ExchangeName { get; set; } = "showtime.order-ticket.events";
    [Required, StringLength(200)] public string OrderNotificationQueueName { get; set; } = "showtime.order.notifications.v1";
    [Required, StringLength(200)] public string DeadLetterExchangeName { get; set; } = "showtime.order-ticket.dlx";
    [Required, StringLength(200)] public string DeadLetterQueueName { get; set; } = "showtime.order.notifications.dlq.v1";
    [Range(1, 500)] public int PublishBatchSize { get; set; } = 50;
    [Range(1, 3600)] public int OutboxPollIntervalSeconds { get; set; } = 5;
    [Range(1, 300)] public int ProcessingLeaseSeconds { get; set; } = 30;
    [Range(1, 1000)] public ushort PrefetchCount { get; set; } = 16;
    [Range(1, 100)] public int MaxPublishAttempts { get; set; } = 8;
    [Range(1, 86400)] public int MaxBackoffSeconds { get; set; } = 300;
    [Range(0, 20)] public int ConsumerMaxRetries { get; set; } = 3;
}

public sealed class RabbitMqOptionsValidator(IConfiguration configuration)
    : IValidateOptions<RabbitMqOptions>
{
    public ValidateOptionsResult Validate(string? name, RabbitMqOptions options)
    {
        if (options.Enabled && string.IsNullOrWhiteSpace(
                configuration.GetConnectionString("RabbitMq")))
        {
            return ValidateOptionsResult.Fail(
                "ConnectionStrings:RabbitMq is required when RabbitMq:Enabled=true.");
        }

        return ValidateOptionsResult.Success;
    }
}

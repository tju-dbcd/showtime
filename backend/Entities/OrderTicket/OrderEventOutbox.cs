using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.OrderTicket;

public sealed class OrderEventOutbox : AuditableEntity
{
    public string EventId { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public string RoutingKey { get; set; } = null!;
    public long AggregateId { get; set; }
    public long UserId { get; set; }
    public string Payload { get; set; } = null!;
    public DateTime OccurredAt { get; set; }
    public string Status { get; set; } = "PENDING";
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? LockedUntil { get; set; }
    public string? LockOwner { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? LastError { get; set; }
}

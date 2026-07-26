using ShowtimeBackend.Entities.Base;

namespace ShowtimeBackend.Entities.SeatZone;

/// <summary>
/// Stores a seat reservation after a lock is converted or a system reservation is created.
/// Order-item references remain scalar IDs until the order-ticket module is mapped.
/// </summary>
public class SeatReservation : AuditableEntity
{
    public long SeatReservationId { get; set; }
    public long SessionId { get; set; }
    public long SeatId { get; set; }
    public long? OrderItemId { get; set; }
    public long? SeatLockId { get; set; }
    public string ReservationType { get; set; } = "ORDER";
    public string ReservationStatus { get; set; } = "ACTIVE";
    public DateTime ReserveTime { get; set; }
    public DateTime? CancelTime { get; set; }
    public string? HoldReason { get; set; }
    public string? Remark { get; set; }
}

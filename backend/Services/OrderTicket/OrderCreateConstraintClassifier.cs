using Microsoft.EntityFrameworkCore;

namespace ShowtimeBackend.Services.OrderTicket;

internal enum OrderCreateUniqueConstraint
{
    None,
    IdempotencyKey,
    SeatReservation,
    Other,
}

internal static class OrderCreateConstraintClassifier
{
    public static OrderCreateUniqueConstraint Classify(DbUpdateException exception)
    {
        var messages = EnumerateMessages(exception);
        if (messages.Any(message =>
                message.Contains(
                    "UK_T_ORDER_USER_IDEMPOTENCY",
                    StringComparison.OrdinalIgnoreCase) ||
                ContainsSqliteIdempotencyColumns(message)))
        {
            return OrderCreateUniqueConstraint.IdempotencyKey;
        }

        if (messages.Any(IsSeatReservationConstraintFailure))
        {
            return OrderCreateUniqueConstraint.SeatReservation;
        }

        return messages.Any(IsUniqueConstraintFailure)
            ? OrderCreateUniqueConstraint.Other
            : OrderCreateUniqueConstraint.None;
    }

    private static IReadOnlyList<string> EnumerateMessages(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception;
             current is not null;
             current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return messages;
    }

    private static bool ContainsSqliteIdempotencyColumns(string message) =>
        message.Contains(
            "UNIQUE constraint failed:",
            StringComparison.OrdinalIgnoreCase) &&
        message.Contains("T_ORDER.USER_ID", StringComparison.OrdinalIgnoreCase) &&
        message.Contains(
            "T_ORDER.IDEMPOTENCY_KEY",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsUniqueConstraintFailure(string message) =>
        message.Contains("ORA-00001", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase);

    private static bool IsSeatReservationConstraintFailure(string message) =>
        message.Contains(
            "UK_SEAT_RESERVATION_ORDER_ITEM",
            StringComparison.OrdinalIgnoreCase) ||
        message.Contains(
            "UK_SEAT_RESERVATION_LOCK",
            StringComparison.OrdinalIgnoreCase) ||
        message.Contains(
            "UK_SEAT_RESERVATION_ACTIVE",
            StringComparison.OrdinalIgnoreCase) ||
        message.Contains(
            "SEAT_RESERVATION.ORDER_ITEM_ID",
            StringComparison.OrdinalIgnoreCase) ||
        message.Contains(
            "SEAT_RESERVATION.SEAT_LOCK_ID",
            StringComparison.OrdinalIgnoreCase);
}

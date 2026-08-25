using Microsoft.EntityFrameworkCore;

namespace ShowtimeBackend.Services.OrderTicket;

public enum TicketUniqueConstraint
{
    Other,
    OrderItem,
    TicketNumber,
    QrCode,
    AntiFakeCode,
}

public static class TicketConstraintClassifier
{
    public static TicketUniqueConstraint Classify(DbUpdateException exception)
    {
        var messages = EnumerateMessages(exception);
        if (Contains(messages, "UK_ETICKET_ORDERITEM") ||
            Contains(messages, "E_TICKET.ORDER_ITEM_ID"))
        {
            return TicketUniqueConstraint.OrderItem;
        }

        if (Contains(messages, "UK_ETICKET_NO") ||
            Contains(messages, "E_TICKET.ETICKET_NO"))
        {
            return TicketUniqueConstraint.TicketNumber;
        }

        if (Contains(messages, "UK_ETICKET_QRCODE") ||
            Contains(messages, "E_TICKET.QR_CODE"))
        {
            return TicketUniqueConstraint.QrCode;
        }

        if (Contains(messages, "UK_ETICKET_ANTIFAKE") ||
            Contains(messages, "E_TICKET.ANTI_FAKE_CODE"))
        {
            return TicketUniqueConstraint.AntiFakeCode;
        }

        return TicketUniqueConstraint.Other;
    }

    private static IReadOnlyList<string> EnumerateMessages(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return messages;
    }

    private static bool Contains(
        IEnumerable<string> messages,
        string value) => messages.Any(
            message => message.Contains(value, StringComparison.OrdinalIgnoreCase));
}

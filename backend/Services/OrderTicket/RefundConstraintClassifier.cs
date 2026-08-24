using Microsoft.EntityFrameworkCore;

namespace ShowtimeBackend.Services.OrderTicket;

public enum RefundUniqueConstraint
{
    None,
    OrderItem,
    RefundNumber,
    Other,
}

public static class RefundConstraintClassifier
{
    public static RefundUniqueConstraint Classify(DbUpdateException exception)
    {
        var messages = EnumerateMessages(exception);
        if (!messages.Any(IsUniqueConstraintFailure))
        {
            return RefundUniqueConstraint.None;
        }

        if (ContainsConstraintName(messages, "UK_REFUND_ORDER_ITEM"))
        {
            return RefundUniqueConstraint.OrderItem;
        }

        if (ContainsConstraintName(messages, "UK_REFUND_NO"))
        {
            return RefundUniqueConstraint.RefundNumber;
        }

        return RefundUniqueConstraint.Other;
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

    private static bool IsUniqueConstraintFailure(string message) =>
        message.Contains("ORA-00001", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsConstraintName(
        IEnumerable<string> messages,
        string constraintName) => messages.Any(
            message => ContainsExactIdentifier(message, constraintName));

    private static bool ContainsExactIdentifier(string message, string identifier)
    {
        var startIndex = 0;
        while (startIndex < message.Length)
        {
            var matchIndex = message.IndexOf(
                identifier,
                startIndex,
                StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
            {
                return false;
            }

            var beforeIsIdentifier = matchIndex > 0 &&
                IsIdentifierCharacter(message[matchIndex - 1]);
            var afterIndex = matchIndex + identifier.Length;
            var afterIsIdentifier = afterIndex < message.Length &&
                IsIdentifierCharacter(message[afterIndex]);
            if (!beforeIsIdentifier && !afterIsIdentifier)
            {
                return true;
            }

            startIndex = matchIndex + identifier.Length;
        }

        return false;
    }

    private static bool IsIdentifierCharacter(char value) =>
        char.IsLetterOrDigit(value) || value == '_';
}

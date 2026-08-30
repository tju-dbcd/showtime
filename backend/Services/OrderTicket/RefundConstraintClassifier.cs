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
        var sawUniqueConstraintFailure = false;
        foreach (var message in messages)
        {
            if (!TryReadUniqueConstraintName(message, out var constraintName))
            {
                sawUniqueConstraintFailure |= IsUniqueConstraintFailure(message);
                continue;
            }

            sawUniqueConstraintFailure = true;
            if (constraintName.Equals(
                    "UK_REFUND_ORDER_ITEM",
                    StringComparison.OrdinalIgnoreCase))
            {
                return RefundUniqueConstraint.OrderItem;
            }

            if (constraintName.Equals(
                    "UK_REFUND_NO",
                    StringComparison.OrdinalIgnoreCase))
            {
                return RefundUniqueConstraint.RefundNumber;
            }
        }

        return sawUniqueConstraintFailure
            ? RefundUniqueConstraint.Other
            : RefundUniqueConstraint.None;
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

    private static bool TryReadUniqueConstraintName(
        string message,
        out string constraintName) =>
        TryReadOracleUniqueConstraintName(message, out constraintName) ||
        TryReadSqliteUniqueConstraintName(message, out constraintName);

    private static bool TryReadOracleUniqueConstraintName(
        string message,
        out string constraintName)
    {
        constraintName = string.Empty;
        var oracleMarker = message.IndexOf(
            "ORA-00001",
            StringComparison.OrdinalIgnoreCase);
        if (oracleMarker < 0)
        {
            return false;
        }

        var uniqueMarker = message.IndexOf(
            "unique constraint",
            oracleMarker,
            StringComparison.OrdinalIgnoreCase);
        if (uniqueMarker < 0)
        {
            return false;
        }

        var openParenthesis = message.IndexOf('(', uniqueMarker);
        var closeParenthesis = openParenthesis < 0
            ? -1
            : message.IndexOf(')', openParenthesis + 1);
        if (openParenthesis < 0 || closeParenthesis <= openParenthesis + 1)
        {
            return false;
        }

        var qualifiedName = message[(openParenthesis + 1)..closeParenthesis]
            .Trim();
        var separator = qualifiedName.LastIndexOf('.');
        constraintName = qualifiedName[(separator + 1)..]
            .Trim()
            .Trim('"');
        return constraintName.Length > 0 &&
            constraintName.All(IsOracleIdentifierCharacter);
    }

    private static bool TryReadSqliteUniqueConstraintName(
        string message,
        out string constraintName)
    {
        constraintName = string.Empty;
        const string marker = "UNIQUE constraint failed:";
        var markerIndex = message.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return false;
        }

        constraintName = message[(markerIndex + marker.Length)..].Trim();
        return constraintName.Length > 0;
    }

    private static bool IsOracleIdentifierCharacter(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '$' or '#';
}

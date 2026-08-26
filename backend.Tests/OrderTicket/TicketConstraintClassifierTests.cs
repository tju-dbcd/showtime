using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class TicketConstraintClassifierTests
{
    [Theory]
    [InlineData("UK_ETICKET_ORDERITEM", TicketUniqueConstraint.OrderItem)]
    [InlineData("UK_ETICKET_NO", TicketUniqueConstraint.TicketNumber)]
    [InlineData("UK_ETICKET_QRCODE", TicketUniqueConstraint.QrCode)]
    [InlineData("UK_ETICKET_ANTIFAKE", TicketUniqueConstraint.AntiFakeCode)]
    public void Classify_RecognizesExactOracleConstraintName(
        string constraintName,
        TicketUniqueConstraint expected)
    {
        var exception = Wrap(
            $"ORA-00001: unique constraint (APP_OWNER.{constraintName}) violated");

        Assert.Equal(expected, TicketConstraintClassifier.Classify(exception));
    }

    [Theory]
    [InlineData("E_TICKET.ORDER_ITEM_ID", TicketUniqueConstraint.OrderItem)]
    [InlineData("E_TICKET.ETICKET_NO", TicketUniqueConstraint.TicketNumber)]
    [InlineData("E_TICKET.QR_CODE", TicketUniqueConstraint.QrCode)]
    [InlineData("E_TICKET.ANTI_FAKE_CODE", TicketUniqueConstraint.AntiFakeCode)]
    public void Classify_RecognizesSqliteUniqueColumn(
        string columnName,
        TicketUniqueConstraint expected)
    {
        var exception = Wrap($"SQLite Error 19: UNIQUE constraint failed: {columnName}");

        Assert.Equal(expected, TicketConstraintClassifier.Classify(exception));
    }

    [Theory]
    [InlineData("ORA-00001: unique constraint (APP_OWNER.UK_PAYMENT_NO) violated")]
    [InlineData("ORA-00001: unique constraint violated")]
    [InlineData("SQLite Error 19: UNIQUE constraint failed: PAYMENT.PAYMENT_NO")]
    [InlineData("some other database failure")]
    public void Classify_DoesNotTreatUnrelatedUniqueFailureAsTicketIdempotency(
        string message)
    {
        Assert.Equal(
            TicketUniqueConstraint.Other,
            TicketConstraintClassifier.Classify(Wrap(message)));
    }

    private static DbUpdateException Wrap(string message) =>
        new("Save failed.", new InvalidOperationException(message));
}

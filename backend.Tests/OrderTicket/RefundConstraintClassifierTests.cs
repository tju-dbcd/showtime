using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class RefundConstraintClassifierTests
{
    [Theory]
    [InlineData(
        "ORA-00001: unique constraint (APP_OWNER.UK_REFUND_ORDER_ITEM) violated")]
    [InlineData(
        "SQLite Error 19: UNIQUE constraint failed: UK_REFUND_ORDER_ITEM")]
    public void Classify_RecognizesOnlyExplicitOrderItemConstraint(string message)
    {
        Assert.Equal(
            RefundUniqueConstraint.OrderItem,
            RefundConstraintClassifier.Classify(Wrap(message)));
    }

    [Theory]
    [InlineData("ORA-00001: unique constraint (APP_OWNER.UK_REFUND_NO) violated")]
    [InlineData("SQLite Error 19: UNIQUE constraint failed: UK_REFUND_NO")]
    public void Classify_RecognizesRefundNumberConstraint(string message)
    {
        Assert.Equal(
            RefundUniqueConstraint.RefundNumber,
            RefundConstraintClassifier.Classify(Wrap(message)));
    }

    [Theory]
    [InlineData("ORA-00001: unique constraint (APP_OWNER.UK_PAYMENT_NO) violated")]
    [InlineData("SQLite Error 19: UNIQUE constraint failed: REFUND_ITEM.ORDER_ITEM_ID")]
    [InlineData("ORA-00001: unique constraint (APP_OWNER.UK_REFUND_ORDER_ITEM_COPY) violated")]
    [InlineData("ORA-00001: unique constraint (APP_OWNER.UK_REFUND_ORDER_ITEM$ARCHIVE) violated")]
    [InlineData("ORA-00001: unique constraint (APP_OWNER.UK_REFUND_ORDER_ITEM#ARCHIVE) violated")]
    public void Classify_LeavesUnknownUniqueConstraintAsOther(string message)
    {
        Assert.Equal(
            RefundUniqueConstraint.Other,
            RefundConstraintClassifier.Classify(Wrap(message)));
    }

    [Fact]
    public void Classify_DoesNotCombineWrapperTargetWithDifferentInnerConstraint()
    {
        var exception = new DbUpdateException(
            "Saving target UK_REFUND_ORDER_ITEM failed.",
            new InvalidOperationException(
                "ORA-00001: unique constraint (APP_OWNER.UK_REFUND_NO) violated"));

        Assert.Equal(
            RefundUniqueConstraint.RefundNumber,
            RefundConstraintClassifier.Classify(exception));
    }

    [Theory]
    [InlineData("ORA-02291: integrity constraint violated - parent key not found")]
    [InlineData("some other database failure")]
    public void Classify_ReturnsNoneForNonUniqueDatabaseFailure(string message)
    {
        Assert.Equal(
            RefundUniqueConstraint.None,
            RefundConstraintClassifier.Classify(Wrap(message)));
    }

    private static DbUpdateException Wrap(string message) =>
        new("Save failed.", new InvalidOperationException(message));
}

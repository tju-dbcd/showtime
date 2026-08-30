using ShowtimeBackend.Common;
using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class RefundPolicyEngineTests
{
    [Fact]
    public void Quote_PrefersMatchingShowPolicyAndUsesExactDeadline()
    {
        var input = RefundFixtures.QuoteInput(
            applicationTime: new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc),
            sessionStartTime: new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc),
            policies:
            [
                new(1, null, "全局48小时", 48, 0.9m, 0m, 1, 1),
                new(2, 90, "演出72小时", 72, 0.8m, 5m, 1, 1),
            ]);

        var result = new RefundPolicyEngine().Quote(input);

        Assert.NotNull(result);
        Assert.Equal(2, result.AppliedPolicyId);
        Assert.Equal(163m, result.ActualRefund);
    }

    [Fact]
    public void Quote_DoesNotMatchPolicyWhenApplicationIsOneTickBeforeDeadline()
    {
        var input = RefundFixtures.QuoteInput(
            sessionStartTime: new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc),
            applicationTime: new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc).AddTicks(1),
            policies:
            [
                new(1, null, "全局48小时", 48, 0.9m, 0m, 1, 1),
                new(2, 90, "演出72小时", 72, 0.8m, 5m, 1, 1),
            ]);

        var result = new RefundPolicyEngine().Quote(input);

        Assert.NotNull(result);
        Assert.Equal(1, result.AppliedPolicyId);
        Assert.Equal(189m, result.ActualRefund);
    }

    [Fact]
    public void Quote_FallsBackToGlobalPolicyWhenNoShowSpecificPolicyMatches()
    {
        var input = RefundFixtures.QuoteInput(
            policies:
            [
                new(1, null, "全局48小时", 48, 0.9m, 0m, 1, 1),
                new(2, 90, "演出96小时", 96, 0.8m, 5m, 1, 1),
            ]);

        var result = new RefundPolicyEngine().Quote(input);

        Assert.NotNull(result);
        Assert.Equal(1, result.AppliedPolicyId);
        Assert.Equal(189m, result.ActualRefund);
    }

    [Fact]
    public void Quote_OrdersEqualDeadlinePoliciesByPriorityThenPolicyId()
    {
        var input = RefundFixtures.QuoteInput(
            policies:
            [
                new(3, 90, "优先级2", 72, 0.7m, 0m, 2, 1),
                new(4, 90, "优先级1编号4", 72, 0.8m, 0m, 1, 1),
                new(2, 90, "优先级1编号2", 72, 0.9m, 0m, 1, 1),
            ]);

        var result = new RefundPolicyEngine().Quote(input);

        Assert.NotNull(result);
        Assert.Equal(2, result.AppliedPolicyId);
        Assert.Equal(189m, result.ActualRefund);
    }

    [Fact]
    public void Quote_ReturnsNullWhenNoPolicyMatches()
    {
        var input = RefundFixtures.QuoteInput(
            policies: [new(1, null, "全局96小时", 96, 0.9m, 0m, 1, 1)]);

        var result = new RefundPolicyEngine().Quote(input);

        Assert.Null(result);
    }

    [Fact]
    public void Quote_AllocatesNetPaidByLargestRemainderWithStableTieBreak()
    {
        var input = RefundFixtures.QuoteInput(
            netPaid: 100m,
            items: [new(101, 1m), new(102, 1m), new(103, 1m)],
            selectedIds: [101L, 102L, 103L],
            policies: [new(1, null, "全额", 0, 1m, 0m, 1, 1)]);

        var result = new RefundPolicyEngine().Quote(input)!;

        Assert.Equal(33.34m, result.Items.Single(item => item.OrderItemId == 101).RefundBaseAmount);
        Assert.Equal(33.33m, result.Items.Single(item => item.OrderItemId == 102).RefundBaseAmount);
        Assert.Equal(33.33m, result.Items.Single(item => item.OrderItemId == 103).RefundBaseAmount);
        Assert.Equal(100m, result.Items.Sum(item => item.RefundBaseAmount));
    }

    [Fact]
    public void Quote_AppliesFixedServiceFeeOnlyOnce()
    {
        var input = RefundFixtures.QuoteInput(
            policies: [new(1, null, "半额", 0, 0.5m, 10m, 1, 1)]);

        var result = new RefundPolicyEngine().Quote(input)!;

        Assert.Equal(210m, result.RefundAmount);
        Assert.Equal(10m, result.AppliedServiceFee);
        Assert.Equal(95m, result.ActualRefund);
    }

    [Fact]
    public void Quote_RoundsActualRefundAwayFromZero()
    {
        var input = RefundFixtures.QuoteInput(
            netPaid: 0.05m,
            items: [new(101, 0.05m)],
            selectedIds: [101L],
            policies: [new(1, null, "半额", 0, 0.5m, 0m, 1, 1)]);

        var result = new RefundPolicyEngine().Quote(input)!;

        Assert.Equal(0.03m, result.ActualRefund);
    }

    [Fact]
    public void Quote_DerivesPartRefundFromSelectedOrderItemIds()
    {
        var input = RefundFixtures.QuoteInput(selectedIds: [101L]);

        var result = new RefundPolicyEngine().Quote(input)!;

        Assert.Equal(RefundType.PART, result.RefundType);
        Assert.Equal(105m, result.RefundAmount);
        Assert.Equal(105m, result.ActualRefund);
    }

    [Fact]
    public void Quote_DerivesFullRefundFromAllValidatedSelectedOrderItemIds()
    {
        var result = new RefundPolicyEngine().Quote(RefundFixtures.QuoteInput())!;

        Assert.Equal(RefundType.FULL, result.RefundType);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(210m, result.RefundAmount);
    }

    [Fact]
    public void Quote_ThrowsArgumentExceptionWhenOrderItemTotalIsZero()
    {
        var input = RefundFixtures.QuoteInput(
            items: [new(101, 0m), new(102, 0m)],
            selectedIds: [101L, 102L]);

        Assert.Throws<ArgumentException>(() => new RefundPolicyEngine().Quote(input));
    }

    [Fact]
    public void Quote_ThrowsArgumentExceptionForZeroOrderItemTotalWhenNoPolicyMatches()
    {
        var input = RefundFixtures.QuoteInput(
            items: [new(101, 0m), new(102, 0m)],
            selectedIds: [101L, 102L],
            policies: [new(1, null, "全局96小时", 96, 1m, 0m, 1, 1)]);

        Assert.Throws<ArgumentException>(() => new RefundPolicyEngine().Quote(input));
    }

    [Fact]
    public void Quote_ThrowsArgumentExceptionWhenNoOrderItemIsSelected()
    {
        var input = RefundFixtures.QuoteInput(selectedIds: Array.Empty<long>());

        Assert.Throws<ArgumentException>(() => new RefundPolicyEngine().Quote(input));
    }

    [Fact]
    public void Quote_ThrowsArgumentExceptionWhenSelectedOrderItemIdsContainDuplicates()
    {
        var input = RefundFixtures.QuoteInput(selectedIds: [101L, 101L]);

        Assert.Throws<ArgumentException>(() => new RefundPolicyEngine().Quote(input));
    }

    [Fact]
    public void Quote_ThrowsArgumentExceptionWhenSelectedOrderItemIdsContainUnknownItem()
    {
        var input = RefundFixtures.QuoteInput(selectedIds: [999L]);

        Assert.Throws<ArgumentException>(() => new RefundPolicyEngine().Quote(input));
    }

    [Fact]
    public void Quote_ThrowsArgumentExceptionWhenAllItemsContainDuplicateOrderItemIds()
    {
        var input = RefundFixtures.QuoteInput(
            items: [new(101, 105m), new(101, 105m)],
            selectedIds: [101L]);

        Assert.Throws<ArgumentException>(() => new RefundPolicyEngine().Quote(input));
    }
}

internal static class RefundFixtures
{
    public static RefundQuoteInput QuoteInput(
        DateTime? applicationTime = null,
        DateTime? sessionStartTime = null,
        decimal netPaid = 210m,
        IReadOnlyList<RefundAllocationItem>? items = null,
        IReadOnlyCollection<long>? selectedIds = null,
        IReadOnlyList<RefundPolicyRule>? policies = null) => new(
        applicationTime ?? new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc),
        sessionStartTime ?? new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc),
        90,
        netPaid,
        items ?? [new(101, 105m), new(102, 105m)],
        selectedIds ?? [101L, 102L],
        policies ?? [new(1, null, "全局", 0, 1m, 0m, 1, 1)]);
}

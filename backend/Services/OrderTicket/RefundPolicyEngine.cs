using ShowtimeBackend.Common;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed record RefundPolicyRule(
    long PolicyId,
    long? ShowId,
    string PolicyName,
    int RefundDeadlineHour,
    decimal RefundRate,
    decimal ServiceFee,
    int Priority,
    byte Status);

public sealed record RefundAllocationItem(long OrderItemId, decimal UnitPrice);

public sealed record RefundQuoteLine(long OrderItemId, decimal RefundBaseAmount);

public sealed record RefundQuoteInput(
    DateTime ApplicationTime,
    DateTime SessionStartTime,
    long ShowId,
    decimal NetPaid,
    IReadOnlyList<RefundAllocationItem> AllItems,
    IReadOnlyCollection<long> SelectedOrderItemIds,
    IReadOnlyList<RefundPolicyRule> Policies);

public sealed record RefundPolicyQuote(
    DateTime QuotedAt,
    RefundType RefundType,
    long AppliedPolicyId,
    string PolicyName,
    decimal RefundAmount,
    decimal FeeRate,
    decimal AppliedServiceFee,
    decimal ActualRefund,
    IReadOnlyList<RefundQuoteLine> Items);

public sealed class RefundPolicyEngine
{
    public RefundPolicyQuote? Quote(RefundQuoteInput input)
    {
        var denominator = input.AllItems.Sum(item => item.UnitPrice);
        if (denominator <= 0m)
            throw new ArgumentException("Order item total must be positive.", nameof(input));

        var allItemIds = input.AllItems.Select(item => item.OrderItemId).ToHashSet();
        if (allItemIds.Count != input.AllItems.Count)
            throw new ArgumentException("Order item IDs must be unique.", nameof(input));

        if (input.SelectedOrderItemIds.Count == 0)
            throw new ArgumentException("At least one order item must be selected.", nameof(input));

        var selectedItemIds = input.SelectedOrderItemIds.ToHashSet();
        if (selectedItemIds.Count != input.SelectedOrderItemIds.Count)
            throw new ArgumentException("Selected order item IDs must be unique.", nameof(input));

        if (!selectedItemIds.IsSubsetOf(allItemIds))
            throw new ArgumentException("Selected order item IDs must belong to the order.", nameof(input));

        var policy = SelectPolicy(input);
        if (policy is null)
            return null;

        var allocated = input.AllItems
            .Select(item => new
            {
                item.OrderItemId,
                Raw = input.NetPaid * item.UnitPrice / denominator,
            })
            .Select(item => new
            {
                item.OrderItemId,
                item.Raw,
                Floor = decimal.Floor(item.Raw * 100m) / 100m,
            })
            .ToList();
        var cents = decimal.ToInt32((input.NetPaid - allocated.Sum(item => item.Floor)) * 100m);
        var bonusIds = allocated
            .OrderByDescending(item => item.Raw - item.Floor)
            .ThenBy(item => item.OrderItemId)
            .Take(cents)
            .Select(item => item.OrderItemId)
            .ToHashSet();
        var lines = allocated
            .Where(item => selectedItemIds.Contains(item.OrderItemId))
            .Select(item => new RefundQuoteLine(
                item.OrderItemId,
                item.Floor + (bonusIds.Contains(item.OrderItemId) ? 0.01m : 0m)))
            .ToList();
        var refundAmount = lines.Sum(item => item.RefundBaseAmount);
        var actualRefund = decimal.Round(
            refundAmount * policy.RefundRate - policy.ServiceFee,
            2,
            MidpointRounding.AwayFromZero);

        return new RefundPolicyQuote(
            input.ApplicationTime,
            selectedItemIds.SetEquals(allItemIds) ? RefundType.FULL : RefundType.PART,
            policy.PolicyId,
            policy.PolicyName,
            refundAmount,
            policy.RefundRate,
            policy.ServiceFee,
            actualRefund,
            lines);
    }

    private static RefundPolicyRule? SelectPolicy(RefundQuoteInput input)
    {
        var matchingPolicies = input.Policies.Where(policy =>
            input.SessionStartTime - input.ApplicationTime >= TimeSpan.FromHours(policy.RefundDeadlineHour));
        var showPolicies = matchingPolicies.Where(policy => policy.ShowId == input.ShowId);
        var applicablePolicies = showPolicies.Any()
            ? showPolicies
            : matchingPolicies.Where(policy => policy.ShowId is null);

        return applicablePolicies
            .OrderByDescending(policy => policy.RefundDeadlineHour)
            .ThenBy(policy => policy.Priority)
            .ThenBy(policy => policy.PolicyId)
            .FirstOrDefault();
    }
}

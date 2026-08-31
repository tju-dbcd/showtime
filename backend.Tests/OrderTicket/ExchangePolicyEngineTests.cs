using ShowtimeBackend.Services.OrderTicket;

namespace ShowtimeBackend.Tests.OrderTicket;

public sealed class ExchangePolicyEngineTests
{
    private static readonly DateTime Now =
        new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Select_PrefersShowPolicyAndAcceptsExactDeadline()
    {
        var result = new ExchangePolicyEngine().Select(new ExchangePolicyInput(
            Now,
            Now.AddHours(72),
            90,
            true,
            [
                Rule(1, null, deadline: 24),
                Rule(2, 90, deadline: 72),
            ]));

        Assert.Equal(2, result!.PolicyId);
    }

    [Fact]
    public void Select_OneTickInsideDeadlineFallsBackToGlobalPolicy()
    {
        var result = new ExchangePolicyEngine().Select(new ExchangePolicyInput(
            Now.AddTicks(1),
            Now.AddHours(72),
            90,
            true,
            [
                Rule(1, null, deadline: 24),
                Rule(2, 90, deadline: 72),
            ]));

        Assert.Equal(1, result!.PolicyId);
    }

    [Fact]
    public void Select_UsesPriorityDescendingThenPolicyIdAscending()
    {
        var result = new ExchangePolicyEngine().Select(new ExchangePolicyInput(
            Now,
            Now.AddDays(5),
            90,
            false,
            [Rule(8, 90, priority: 3), Rule(7, 90, priority: 3), Rule(6, 90, priority: 2)]));

        Assert.Equal(7, result!.PolicyId);
    }

    [Fact]
    public void Select_RejectsCrossSessionWhenPolicyDisallowsIt()
    {
        var result = new ExchangePolicyEngine().Select(new ExchangePolicyInput(
            Now,
            Now.AddDays(5),
            90,
            true,
            [Rule(1, 90, allowCrossSession: 0)]));

        Assert.Null(result);
    }

    [Fact]
    public void Select_IgnoresDisabledPolicies()
    {
        var result = new ExchangePolicyEngine().Select(new ExchangePolicyInput(
            Now,
            Now.AddDays(5),
            90,
            false,
            [Rule(1, 90, status: 0)]));

        Assert.Null(result);
    }

    private static ExchangePolicyRule Rule(
        long id,
        long? showId,
        int deadline = 24,
        byte allowCrossSession = 1,
        int priority = 1,
        byte status = 1) =>
        new(id, showId, $"policy-{id}", deadline, 10m, allowCrossSession, priority, status);
}

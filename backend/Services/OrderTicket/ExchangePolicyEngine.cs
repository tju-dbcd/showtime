namespace ShowtimeBackend.Services.OrderTicket;

public sealed record ExchangePolicyRule(
    long PolicyId,
    long? ShowId,
    string PolicyName,
    int ExchangeDeadlineHour,
    decimal ExchangeFee,
    byte AllowCrossSession,
    int Priority,
    byte Status);

public sealed record ExchangePolicyInput(
    DateTime OperationTime,
    DateTime OriginalSessionStartTime,
    long OriginalShowId,
    bool IsCrossSession,
    IReadOnlyList<ExchangePolicyRule> Policies);

public sealed class ExchangePolicyEngine
{
    public ExchangePolicyRule? Select(ExchangePolicyInput input)
    {
        var eligible = input.Policies.Where(policy =>
            policy.Status == 1 &&
            (!input.IsCrossSession || policy.AllowCrossSession == 1) &&
            input.OriginalSessionStartTime - input.OperationTime >=
                TimeSpan.FromHours(policy.ExchangeDeadlineHour));

        var showPolicies = eligible.Where(policy => policy.ShowId == input.OriginalShowId);
        var candidates = showPolicies.Any()
            ? showPolicies
            : eligible.Where(policy => policy.ShowId is null);

        return candidates
            .OrderByDescending(policy => policy.Priority)
            .ThenBy(policy => policy.PolicyId)
            .FirstOrDefault();
    }
}

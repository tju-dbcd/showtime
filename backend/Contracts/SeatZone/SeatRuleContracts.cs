namespace ShowtimeBackend.Contracts.SeatZone;

public sealed record SeatRuleRequest(
    string RuleCode,
    string RuleName,
    string RuleType,
    int MinSeatCount,
    int MaxSeatCount,
    bool AllowCrossRow,
    bool AllowCrossSection,
    int Priority,
    string RuleStatus,
    string? Remark);

public sealed record SeatRuleResponse(
    long SeatRuleId,
    string RuleCode,
    string RuleName,
    string RuleType,
    int MinSeatCount,
    int MaxSeatCount,
    bool AllowCrossRow,
    bool AllowCrossSection,
    int Priority,
    string RuleStatus,
    string? Remark);

public sealed record SeatRuleListQuery(
    string? RuleType,
    string? RuleStatus,
    int Page = 1,
    int PageSize = 20);

public sealed record SeatRuleScopeRequest(
    string ScopeType,
    long? SeatMapId,
    long? SeatSectionId,
    string ScopeStatus);

public sealed record SeatRuleScopeResponse(
    long RuleScopeId,
    long SeatRuleId,
    string ScopeType,
    long? SeatMapId,
    long? SeatSectionId,
    string ScopeStatus);

using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
using ShowtimeBackend.DTOs;
using ShowtimeBackend.DTOs.SeatZone;
using ShowtimeBackend.Data;
using ShowtimeBackend.Entities.SeatZone;

namespace ShowtimeBackend.Services.SeatZone;

public sealed class SeatRuleAdminService
{
    private static readonly HashSet<string> RuleTypes = ["CONTINUOUS", "NO_SINGLE_LEFT", "LIMIT_COUNT", "SECTION_LIMIT"];
    private static readonly HashSet<string> Statuses = ["ENABLED", "DISABLED"];
    private static readonly HashSet<string> ScopeTypes = ["MAP", "SECTION"];
    private readonly AppDbContext _db;

    public SeatRuleAdminService(AppDbContext db) => _db = db;

    public async Task<ServiceResult<PagedResponse<SeatRuleResponse>>> ListRulesAsync(SeatRuleListQuery query, CancellationToken cancellationToken)
    {
        var pagingError = ValidatePaging(query.Page, query.PageSize);
        if (pagingError is not null) return ServiceResult<PagedResponse<SeatRuleResponse>>.Failure(400, "Invalid paging", pagingError);
        if (!string.IsNullOrWhiteSpace(query.RuleType) && !RuleTypes.Contains(query.RuleType))
            return ServiceResult<PagedResponse<SeatRuleResponse>>.Failure(400, "Invalid rule type", "ruleType must be CONTINUOUS, NO_SINGLE_LEFT, LIMIT_COUNT, or SECTION_LIMIT.");
        if (!string.IsNullOrWhiteSpace(query.RuleStatus) && !Statuses.Contains(query.RuleStatus))
            return ServiceResult<PagedResponse<SeatRuleResponse>>.Failure(400, "Invalid rule status", "ruleStatus must be ENABLED or DISABLED.");

        var rules = _db.SeatRules.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.RuleType)) rules = rules.Where(rule => rule.RuleType == query.RuleType);
        if (!string.IsNullOrWhiteSpace(query.RuleStatus)) rules = rules.Where(rule => rule.RuleStatus == query.RuleStatus);
        var totalCount = await rules.CountAsync(cancellationToken);
        var skip = ((long)query.Page - 1) * query.PageSize;
        var items = await rules.OrderBy(rule => rule.RuleCode).ThenBy(rule => rule.SeatRuleId)
            .Skip((int)skip).Take(query.PageSize).Select(rule => ToResponse(rule)).ToListAsync(cancellationToken);
        return ServiceResult<PagedResponse<SeatRuleResponse>>.Success(new PagedResponse<SeatRuleResponse>(items, query.Page, query.PageSize, totalCount));
    }

    public async Task<ServiceResult<SeatRuleResponse>> GetRuleAsync(long seatRuleId, CancellationToken cancellationToken)
    {
        var rule = await _db.SeatRules.AsNoTracking().Where(item => item.SeatRuleId == seatRuleId)
            .Select(item => ToResponse(item)).SingleOrDefaultAsync(cancellationToken);
        return rule is null
            ? ServiceResult<SeatRuleResponse>.Failure(404, "Seat rule not found", $"Seat rule {seatRuleId} does not exist.")
            : ServiceResult<SeatRuleResponse>.Success(rule);
    }

    public async Task<ServiceResult<SeatRuleResponse>> CreateRuleAsync(SeatRuleRequest request, CancellationToken cancellationToken)
    {
        var validation = ValidateRule(request);
        if (validation is not null) return ServiceResult<SeatRuleResponse>.Failure(400, "Invalid seat rule", validation);
        var ruleCode = request.RuleCode.Trim();
        if (await _db.SeatRules.AnyAsync(item => item.RuleCode == ruleCode, cancellationToken))
            return ServiceResult<SeatRuleResponse>.Failure(409, "Duplicate seat rule", "A seat rule with the same ruleCode already exists.");

        var rule = new SeatRule();
        Apply(request, rule);
        _db.SeatRules.Add(rule);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (ContainsOracleError(exception, 1))
        {
            return ServiceResult<SeatRuleResponse>.Failure(409, "Unable to create seat rule", "The seat rule conflicts with existing data.");
        }
        return ServiceResult<SeatRuleResponse>.Success(ToResponse(rule));
    }

    public async Task<ServiceResult<SeatRuleResponse>> UpdateRuleAsync(long seatRuleId, SeatRuleRequest request, CancellationToken cancellationToken)
    {
        var validation = ValidateRule(request);
        if (validation is not null) return ServiceResult<SeatRuleResponse>.Failure(400, "Invalid seat rule", validation);
        var rule = await _db.SeatRules.SingleOrDefaultAsync(item => item.SeatRuleId == seatRuleId, cancellationToken);
        if (rule is null) return ServiceResult<SeatRuleResponse>.Failure(404, "Seat rule not found", $"Seat rule {seatRuleId} does not exist.");
        var ruleCode = request.RuleCode.Trim();
        if (await _db.SeatRules.AnyAsync(item => item.SeatRuleId != seatRuleId && item.RuleCode == ruleCode, cancellationToken))
            return ServiceResult<SeatRuleResponse>.Failure(409, "Duplicate seat rule", "A seat rule with the same ruleCode already exists.");

        Apply(request, rule);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (ContainsOracleError(exception, 1))
        {
            return ServiceResult<SeatRuleResponse>.Failure(409, "Unable to update seat rule", "The seat rule conflicts with existing data.");
        }
        return ServiceResult<SeatRuleResponse>.Success(ToResponse(rule));
    }

    public async Task<ServiceResult<bool>> DeleteRuleAsync(long seatRuleId, CancellationToken cancellationToken)
    {
        var rule = await _db.SeatRules.SingleOrDefaultAsync(item => item.SeatRuleId == seatRuleId, cancellationToken);
        if (rule is null) return ServiceResult<bool>.Failure(404, "Seat rule not found", $"Seat rule {seatRuleId} does not exist.");
        if (await _db.SeatRuleScopes.AnyAsync(item => item.SeatRuleId == seatRuleId, cancellationToken))
            return ServiceResult<bool>.Failure(409, "Seat rule is in use", "Remove rule scopes before deleting this seat rule.");
        _db.SeatRules.Remove(rule);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (ContainsOracleError(exception, 2292))
        {
            return ServiceResult<bool>.Failure(409, "Seat rule is in use", "Remove rule scopes before deleting this seat rule.");
        }
        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<IReadOnlyList<SeatRuleScopeResponse>>> ListScopesAsync(long seatRuleId, CancellationToken cancellationToken)
    {
        if (!await _db.SeatRules.AnyAsync(item => item.SeatRuleId == seatRuleId, cancellationToken))
            return ServiceResult<IReadOnlyList<SeatRuleScopeResponse>>.Failure(404, "Seat rule not found", $"Seat rule {seatRuleId} does not exist.");
        var scopes = await _db.SeatRuleScopes.AsNoTracking().Where(item => item.SeatRuleId == seatRuleId)
            .OrderBy(item => item.RuleScopeId).Select(item => ToResponse(item)).ToListAsync(cancellationToken);
        return ServiceResult<IReadOnlyList<SeatRuleScopeResponse>>.Success(scopes);
    }

    public async Task<ServiceResult<SeatRuleScopeResponse>> CreateScopeAsync(long seatRuleId, SeatRuleScopeRequest request, CancellationToken cancellationToken)
    {
        if (!await _db.SeatRules.AnyAsync(item => item.SeatRuleId == seatRuleId, cancellationToken))
            return ServiceResult<SeatRuleScopeResponse>.Failure(404, "Seat rule not found", $"Seat rule {seatRuleId} does not exist.");
        var validation = ValidateScope(request);
        if (validation is not null) return ServiceResult<SeatRuleScopeResponse>.Failure(400, "Invalid rule scope", validation);
        if (request.ScopeType == "MAP" && !await _db.SeatMaps.AnyAsync(item => item.SeatMapId == request.SeatMapId, cancellationToken))
            return ServiceResult<SeatRuleScopeResponse>.Failure(404, "Seat map not found", $"Seat map {request.SeatMapId} does not exist.");
        if (request.ScopeType == "SECTION" && !await _db.SeatSections.AnyAsync(item => item.SeatSectionId == request.SeatSectionId, cancellationToken))
            return ServiceResult<SeatRuleScopeResponse>.Failure(404, "Seat section not found", $"Seat section {request.SeatSectionId} does not exist.");
        if (await _db.SeatRuleScopes.AnyAsync(item => item.SeatRuleId == seatRuleId &&
            ((request.ScopeType == "MAP" && item.ScopeType == "MAP" && item.SeatMapId == request.SeatMapId) ||
             (request.ScopeType == "SECTION" && item.ScopeType == "SECTION" && item.SeatSectionId == request.SeatSectionId)), cancellationToken))
            return ServiceResult<SeatRuleScopeResponse>.Failure(409, "Duplicate rule scope", "A rule scope with the same target already exists.");

        var scope = new SeatRuleScope { SeatRuleId = seatRuleId };
        Apply(request, scope);
        _db.SeatRuleScopes.Add(scope);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (ContainsOracleError(exception, 1))
        {
            return ServiceResult<SeatRuleScopeResponse>.Failure(409, "Duplicate rule scope", "A rule scope with the same target already exists.");
        }
        return ServiceResult<SeatRuleScopeResponse>.Success(ToResponse(scope));
    }

    public async Task<ServiceResult<bool>> DeleteScopeAsync(long ruleScopeId, CancellationToken cancellationToken)
    {
        var scope = await _db.SeatRuleScopes.SingleOrDefaultAsync(item => item.RuleScopeId == ruleScopeId, cancellationToken);
        if (scope is null) return ServiceResult<bool>.Failure(404, "Seat rule scope not found", $"Seat rule scope {ruleScopeId} does not exist.");
        _db.SeatRuleScopes.Remove(scope);
        await _db.SaveChangesAsync(cancellationToken);
        return ServiceResult<bool>.Success(true);
    }

    private static string? ValidateRule(SeatRuleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RuleCode) || string.IsNullOrWhiteSpace(request.RuleName)) return "ruleCode and ruleName are required.";
        if (request.RuleCode.Length > 30 || request.RuleName.Length > 100) return "ruleCode must be at most 30 characters and ruleName at most 100 characters.";
        if (request.Remark?.Length > 255) return "remark must be at most 255 characters.";
        if (!RuleTypes.Contains(request.RuleType)) return "ruleType must be CONTINUOUS, NO_SINGLE_LEFT, LIMIT_COUNT, or SECTION_LIMIT.";
        if (request.MinSeatCount is < 1 or > 999 || request.MaxSeatCount is < 1 or > 999 || request.MinSeatCount > request.MaxSeatCount) return "minSeatCount and maxSeatCount must be between 1 and 999 and minSeatCount must not exceed maxSeatCount.";
        if (request.Priority is < 0 or > 99999) return "priority must be between 0 and 99999.";
        return Statuses.Contains(request.RuleStatus) ? null : "ruleStatus must be ENABLED or DISABLED.";
    }

    private static string? ValidateScope(SeatRuleScopeRequest request)
    {
        if (!ScopeTypes.Contains(request.ScopeType)) return "scopeType must be MAP or SECTION.";
        if (!Statuses.Contains(request.ScopeStatus)) return "scopeStatus must be ENABLED or DISABLED.";
        if (request.ScopeType == "MAP" && (request.SeatMapId is null || request.SeatSectionId is not null)) return "MAP scopes require seatMapId and no seatSectionId.";
        if (request.ScopeType == "SECTION" && (request.SeatSectionId is null || request.SeatMapId is not null)) return "SECTION scopes require seatSectionId and no seatMapId.";
        return null;
    }

    private static string? ValidatePaging(int page, int pageSize)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100) return "page must be positive and pageSize must be between 1 and 100.";
        return ((long)page - 1) * pageSize > int.MaxValue ? "page and pageSize produce an offset that is too large." : null;
    }

    private static bool ContainsOracleError(Exception exception, int number)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is OracleException oracleException && oracleException.Number == number)
                return true;
        }
        return false;
    }

    private static void Apply(SeatRuleRequest request, SeatRule rule)
    {
        rule.RuleCode = request.RuleCode.Trim();
        rule.RuleName = request.RuleName.Trim();
        rule.RuleType = request.RuleType;
        rule.MinSeatCount = request.MinSeatCount;
        rule.MaxSeatCount = request.MaxSeatCount;
        rule.AllowCrossRow = request.AllowCrossRow;
        rule.AllowCrossSection = request.AllowCrossSection;
        rule.Priority = request.Priority;
        rule.RuleStatus = request.RuleStatus;
        rule.Remark = request.Remark;
    }

    private static void Apply(SeatRuleScopeRequest request, SeatRuleScope scope)
    {
        scope.ScopeType = request.ScopeType;
        scope.SeatMapId = request.SeatMapId;
        scope.SeatSectionId = request.SeatSectionId;
        scope.ScopeStatus = request.ScopeStatus;
    }

    private static SeatRuleResponse ToResponse(SeatRule rule) => new(rule.SeatRuleId, rule.RuleCode, rule.RuleName, rule.RuleType, rule.MinSeatCount, rule.MaxSeatCount, rule.AllowCrossRow, rule.AllowCrossSection, rule.Priority, rule.RuleStatus, rule.Remark);
    private static SeatRuleScopeResponse ToResponse(SeatRuleScope scope) => new(scope.RuleScopeId, scope.SeatRuleId, scope.ScopeType, scope.SeatMapId, scope.SeatSectionId, scope.ScopeStatus);
}

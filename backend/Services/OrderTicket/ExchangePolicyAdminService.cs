using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.ShowSession;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class ExchangePolicyAdminService(AppDbContext dbContext) : IExchangePolicyAdminService
{
    public async Task<OrderTicketResult<PagedExchangePolicyResponse>> ListAsync(
        ExchangePolicyListQuery query,
        CancellationToken cancellationToken)
    {
        var offset = ((long)query.Page - 1) * query.PageSize;
        if (query.Page < 1 || query.PageSize is < 1 or > 100 || offset > int.MaxValue ||
            query.Status is not null and not ((byte)0 or (byte)1) || query.ShowId <= 0)
        {
            return Invalid<PagedExchangePolicyResponse>(
                "EXCHANGE_POLICY_INVALID_PAGING",
                "Page must be positive, pageSize must be between 1 and 100, status must be 0 or 1, and showId must be positive.");
        }

        var policies = dbContext.Set<ExchangePolicy>().AsNoTracking().AsQueryable();
        if (query.ShowId.HasValue)
        {
            policies = policies.Where(item => item.ShowId == query.ShowId.Value);
        }

        if (query.Status.HasValue)
        {
            policies = policies.Where(item => item.Status == query.Status.Value);
        }

        var totalCount = await policies.CountAsync(cancellationToken);
        var items = await policies
            .OrderBy(item => item.ShowId.HasValue)
            .ThenByDescending(item => item.Priority)
            .ThenBy(item => item.PolicyId)
            .Skip((int)offset)
            .Take(query.PageSize)
            .Select(item => ToResponse(item))
            .ToListAsync(cancellationToken);

        return OrderTicketResult<PagedExchangePolicyResponse>.Success(
            new PagedExchangePolicyResponse(items, query.Page, query.PageSize, totalCount));
    }

    public async Task<OrderTicketResult<ExchangePolicyResponse>> CreateAsync(
        string actor,
        SaveExchangePolicyRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateSaveRequestAsync(request, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var entity = new ExchangePolicy
        {
            ShowId = request.ShowId,
            PolicyName = request.PolicyName.Trim(),
            ExchangeDeadlineHour = request.ExchangeDeadlineHour,
            ExchangeFee = request.ExchangeFee,
            AllowCrossSession = request.AllowCrossSession,
            Priority = request.Priority,
            Status = 1,
            Remark = request.Remark,
            CreateBy = actor,
            UpdateBy = actor,
        };
        dbContext.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return OrderTicketResult<ExchangePolicyResponse>.Success(ToResponse(entity));
    }

    public async Task<OrderTicketResult<ExchangePolicyResponse>> UpdateAsync(
        string actor,
        long policyId,
        SaveExchangePolicyRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateSaveRequestAsync(request, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var entity = await dbContext.Set<ExchangePolicy>()
            .SingleOrDefaultAsync(item => item.PolicyId == policyId, cancellationToken);
        if (entity is null)
        {
            return NotFound("EXCHANGE_POLICY_NOT_FOUND", "The exchange policy does not exist.");
        }

        entity.ShowId = request.ShowId;
        entity.PolicyName = request.PolicyName.Trim();
        entity.ExchangeDeadlineHour = request.ExchangeDeadlineHour;
        entity.ExchangeFee = request.ExchangeFee;
        entity.AllowCrossSession = request.AllowCrossSession;
        entity.Priority = request.Priority;
        entity.Remark = request.Remark;
        entity.UpdateBy = actor;
        await dbContext.SaveChangesAsync(cancellationToken);

        return OrderTicketResult<ExchangePolicyResponse>.Success(ToResponse(entity));
    }

    public async Task<OrderTicketResult<ExchangePolicyResponse>> UpdateStatusAsync(
        string actor,
        long policyId,
        UpdateExchangePolicyStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Status is not 0 and not 1)
        {
            return Invalid<ExchangePolicyResponse>(
                "EXCHANGE_POLICY_INVALID_STATUS",
                "Status must be 0 or 1.");
        }

        var entity = await dbContext.Set<ExchangePolicy>()
            .SingleOrDefaultAsync(item => item.PolicyId == policyId, cancellationToken);
        if (entity is null)
        {
            return NotFound("EXCHANGE_POLICY_NOT_FOUND", "The exchange policy does not exist.");
        }

        entity.Status = request.Status;
        entity.UpdateBy = actor;
        await dbContext.SaveChangesAsync(cancellationToken);

        return OrderTicketResult<ExchangePolicyResponse>.Success(ToResponse(entity));
    }

    private async Task<OrderTicketResult<ExchangePolicyResponse>?> ValidateSaveRequestAsync(
        SaveExchangePolicyRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PolicyName) || request.PolicyName.Trim().Length > 100 ||
            request.ExchangeDeadlineHour is < 0 or > 99999 ||
            request.ExchangeFee is < 0m or > 99999999.99m ||
            request.ExchangeFee != decimal.Round(request.ExchangeFee, 2) ||
            request.AllowCrossSession is not 0 and not 1 ||
            request.Priority is <= 0 or > 99999 || request.Remark?.Length > 500 ||
            request.ShowId <= 0)
        {
            return Invalid<ExchangePolicyResponse>(
                "EXCHANGE_POLICY_INVALID",
                "Policy name, deadline, fee, cross-session flag, priority, remark, and show ID are invalid.");
        }

        if (request.ShowId.HasValue && !await dbContext.Set<Show>()
                .AsNoTracking()
                .AnyAsync(item => item.ShowId == request.ShowId.Value, cancellationToken))
        {
            return NotFound("EXCHANGE_POLICY_SHOW_NOT_FOUND", "The requested show does not exist.");
        }

        return null;
    }

    private static ExchangePolicyResponse ToResponse(ExchangePolicy entity) => new(
        entity.PolicyId,
        entity.ShowId,
        entity.PolicyName,
        entity.ExchangeDeadlineHour,
        entity.ExchangeFee,
        entity.AllowCrossSession,
        entity.Priority,
        entity.Status,
        entity.Remark,
        entity.CreateTime,
        entity.UpdateTime);

    private static OrderTicketResult<T> Invalid<T>(string code, string message) =>
        OrderTicketResult<T>.Fail(OrderTicketFailure.InvalidRequest, code, message);

    private static OrderTicketResult<ExchangePolicyResponse> NotFound(string code, string message) =>
        OrderTicketResult<ExchangePolicyResponse>.Fail(OrderTicketFailure.NotFound, code, message);
}

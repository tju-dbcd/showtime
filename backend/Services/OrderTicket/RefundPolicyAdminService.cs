using Microsoft.EntityFrameworkCore;
using ShowtimeBackend.Data;
using ShowtimeBackend.DTOs.OrderTicket;
using ShowtimeBackend.Entities.OrderTicket;
using ShowtimeBackend.Entities.ShowSession;

namespace ShowtimeBackend.Services.OrderTicket;

public sealed class RefundPolicyAdminService(AppDbContext dbContext) : IRefundPolicyAdminService
{
    public async Task<OrderTicketResult<PagedRefundPolicyResponse>> ListAsync(
        RefundPolicyListQuery query,
        CancellationToken cancellationToken)
    {
        var offset = ((long)query.Page - 1) * query.PageSize;
        if (query.Page < 1 || query.PageSize is < 1 or > 100 || offset > int.MaxValue ||
            query.Status is not null and not ((byte)0 or (byte)1))
        {
            return Invalid<PagedRefundPolicyResponse>(
                "REFUND_POLICY_INVALID_PAGING",
                "Page must be positive, pageSize must be between 1 and 100, and status must be 0 or 1.");
        }

        var policies = dbContext.Set<RefundPolicy>().AsNoTracking().AsQueryable();
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
            .OrderByDescending(item => item.RefundDeadlineHour)
            .ThenBy(item => item.Priority)
            .ThenBy(item => item.PolicyId)
            .Skip((int)offset)
            .Take(query.PageSize)
            .Select(item => ToResponse(item))
            .ToListAsync(cancellationToken);

        return OrderTicketResult<PagedRefundPolicyResponse>.Success(
            new PagedRefundPolicyResponse(items, query.Page, query.PageSize, totalCount));
    }

    public async Task<OrderTicketResult<RefundPolicyResponse>> CreateAsync(
        string actor,
        SaveRefundPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateSaveRequestAsync(request, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var entity = new RefundPolicy
        {
            ShowId = request.ShowId,
            PolicyName = request.PolicyName.Trim(),
            RefundDeadlineHour = request.RefundDeadlineHour,
            RefundRate = request.RefundRate,
            ServiceFee = request.ServiceFee,
            Priority = request.Priority,
            Status = 1,
            Remark = request.Remark,
            CreateBy = actor,
            UpdateBy = actor,
        };
        dbContext.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return OrderTicketResult<RefundPolicyResponse>.Success(ToResponse(entity));
    }

    public async Task<OrderTicketResult<RefundPolicyResponse>> UpdateAsync(
        string actor,
        long policyId,
        SaveRefundPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateSaveRequestAsync(request, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var entity = await dbContext.Set<RefundPolicy>()
            .SingleOrDefaultAsync(item => item.PolicyId == policyId, cancellationToken);
        if (entity is null)
        {
            return NotFound("REFUND_POLICY_NOT_FOUND", "The refund policy does not exist.");
        }

        entity.ShowId = request.ShowId;
        entity.PolicyName = request.PolicyName.Trim();
        entity.RefundDeadlineHour = request.RefundDeadlineHour;
        entity.RefundRate = request.RefundRate;
        entity.ServiceFee = request.ServiceFee;
        entity.Priority = request.Priority;
        entity.Remark = request.Remark;
        entity.UpdateBy = actor;
        await dbContext.SaveChangesAsync(cancellationToken);

        return OrderTicketResult<RefundPolicyResponse>.Success(ToResponse(entity));
    }

    public async Task<OrderTicketResult<RefundPolicyResponse>> UpdateStatusAsync(
        string actor,
        long policyId,
        UpdateRefundPolicyStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Status is not 0 and not 1)
        {
            return Invalid<RefundPolicyResponse>(
                "REFUND_POLICY_INVALID_STATUS",
                "Status must be 0 or 1.");
        }

        var entity = await dbContext.Set<RefundPolicy>()
            .SingleOrDefaultAsync(item => item.PolicyId == policyId, cancellationToken);
        if (entity is null)
        {
            return NotFound("REFUND_POLICY_NOT_FOUND", "The refund policy does not exist.");
        }

        entity.Status = request.Status;
        entity.UpdateBy = actor;
        await dbContext.SaveChangesAsync(cancellationToken);

        return OrderTicketResult<RefundPolicyResponse>.Success(ToResponse(entity));
    }

    private async Task<OrderTicketResult<RefundPolicyResponse>?> ValidateSaveRequestAsync(
        SaveRefundPolicyRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PolicyName) || request.PolicyName.Trim().Length > 100 ||
            request.RefundDeadlineHour < 0 || request.RefundRate is < 0m or > 1m ||
            request.ServiceFee < 0m || request.Priority <= 0 || request.Remark?.Length > 500 ||
            request.ShowId <= 0)
        {
            return Invalid<RefundPolicyResponse>(
                "REFUND_POLICY_INVALID",
                "Policy name, deadline, rate, fee, priority, remark, and show ID are invalid.");
        }

        if (request.ShowId.HasValue && !await dbContext.Set<Show>()
                .AsNoTracking()
                .AnyAsync(item => item.ShowId == request.ShowId.Value, cancellationToken))
        {
            return NotFound("REFUND_POLICY_SHOW_NOT_FOUND", "The requested show does not exist.");
        }

        return null;
    }

    private static RefundPolicyResponse ToResponse(RefundPolicy entity) => new(
        entity.PolicyId,
        entity.ShowId,
        entity.PolicyName,
        entity.RefundDeadlineHour,
        entity.RefundRate,
        entity.ServiceFee,
        entity.Priority,
        entity.Status,
        entity.Remark,
        entity.CreateTime,
        entity.UpdateTime);

    private static OrderTicketResult<T> Invalid<T>(string code, string message) =>
        OrderTicketResult<T>.Fail(OrderTicketFailure.InvalidRequest, code, message);

    private static OrderTicketResult<RefundPolicyResponse> NotFound(string code, string message) =>
        OrderTicketResult<RefundPolicyResponse>.Fail(OrderTicketFailure.NotFound, code, message);
}

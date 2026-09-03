using ShowtimeBackend.DTOs.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public interface IRefundPolicyAdminService
{
    Task<OrderTicketResult<PagedRefundPolicyResponse>> ListAsync(
        RefundPolicyListQuery query,
        CancellationToken cancellationToken);

    Task<OrderTicketResult<RefundPolicyResponse>> CreateAsync(
        string actor,
        SaveRefundPolicyRequest request,
        CancellationToken cancellationToken);

    Task<OrderTicketResult<RefundPolicyResponse>> UpdateAsync(
        string actor,
        long policyId,
        SaveRefundPolicyRequest request,
        CancellationToken cancellationToken);

    Task<OrderTicketResult<RefundPolicyResponse>> UpdateStatusAsync(
        string actor,
        long policyId,
        UpdateRefundPolicyStatusRequest request,
        CancellationToken cancellationToken);
}

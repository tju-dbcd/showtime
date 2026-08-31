using ShowtimeBackend.DTOs.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public interface IExchangePolicyAdminService
{
    Task<OrderTicketResult<PagedExchangePolicyResponse>> ListAsync(
        ExchangePolicyListQuery query,
        CancellationToken cancellationToken);

    Task<OrderTicketResult<ExchangePolicyResponse>> CreateAsync(
        string actor,
        SaveExchangePolicyRequest request,
        CancellationToken cancellationToken);

    Task<OrderTicketResult<ExchangePolicyResponse>> UpdateAsync(
        string actor,
        long policyId,
        SaveExchangePolicyRequest request,
        CancellationToken cancellationToken);

    Task<OrderTicketResult<ExchangePolicyResponse>> UpdateStatusAsync(
        string actor,
        long policyId,
        UpdateExchangePolicyStatusRequest request,
        CancellationToken cancellationToken);
}

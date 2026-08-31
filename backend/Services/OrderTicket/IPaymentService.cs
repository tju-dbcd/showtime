using ShowtimeBackend.DTOs.OrderTicket;

namespace ShowtimeBackend.Services.OrderTicket;

public interface IPaymentService
{
    Task<OrderTicketResult<IReadOnlyList<PaymentResponse>>> ListAsync(
        long userId,
        long orderId,
        CancellationToken cancellationToken);

    Task<OrderTicketResult<PaymentProcessResponse>> PayAsync(
        long userId,
        string actor,
        long orderId,
        MockPaymentRequest request,
        CancellationToken cancellationToken);
}

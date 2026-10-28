using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Application.Abstractions.Payments;

public interface IPaymentProvider
{
    Task<PaymentProviderResult> CreatePaymentAsync(
        Guid orderId,
        Money amount,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<PaymentProviderResult> ConfirmAsync(
        Guid paymentId,
        CancellationToken cancellationToken);

    Task<PaymentProviderResult> RefundAsync(
        Guid paymentId,
        Money amount,
        CancellationToken cancellationToken);
}

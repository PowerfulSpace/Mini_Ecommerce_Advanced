using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Payments;

namespace PS.MiniEcommerce.Application.Payments;

public interface IPaymentRefundService
{
    Task RefundAsync(
        Payment payment,
        Money amount,
        string? reason,
        DateTime utcNow,
        CancellationToken cancellationToken);
}

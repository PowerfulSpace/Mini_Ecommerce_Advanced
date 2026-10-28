using PS.MiniEcommerce.Application.Payments;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Payments;

namespace PS.MiniEcommerce.Infrastructure.Payments;

public class PaymentRefundService : IPaymentRefundService
{
    public Task RefundAsync(
        Payment payment,
        Money amount,
        string? reason,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        payment.AddRefund(amount, utcNow, reason);
        return Task.CompletedTask;
    }
}

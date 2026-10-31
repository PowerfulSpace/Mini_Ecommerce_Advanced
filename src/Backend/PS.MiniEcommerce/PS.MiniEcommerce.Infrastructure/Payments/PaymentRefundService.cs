using Microsoft.Extensions.Options;
using PS.MiniEcommerce.Application.Payments;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Payments;

namespace PS.MiniEcommerce.Infrastructure.Payments;

public class PaymentRefundService : IPaymentRefundService
{
    private readonly IOptions<StripeOptions> _stripeOptions;

    public PaymentRefundService(IOptions<StripeOptions> stripeOptions)
    {
        _stripeOptions = stripeOptions;
    }

    public Task RefundAsync(
        Payment payment,
        Money amount,
        string? reason,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var refund = payment.AddRefund(amount, utcNow, reason);

        if (string.Equals(
                payment.Provider,
                _stripeOptions.Value.ProviderName,
                StringComparison.OrdinalIgnoreCase))
        {
            refund.SetProviderRefundId($"re_mock_{Guid.NewGuid()}");
        }

        return Task.CompletedTask;
    }
}

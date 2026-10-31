using Microsoft.Extensions.Options;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Exceptions;
using PS.MiniEcommerce.Domain.Payments;
using PS.MiniEcommerce.Infrastructure.Payments;

namespace PS.MiniEcommerce.UnitTests.Infrastructure;

public class PaymentRefundServiceTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid OrderId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task RefundAsync_WhenPaymentUsesStripeProvider_SetsMockProviderRefundId()
    {
        var payment = CreateConfirmedPayment();
        payment.SetProvider("stripe");
        payment.SetProviderPaymentId("pi_mock_123");

        var service = CreateService(providerName: "stripe");

        await service.RefundAsync(
            payment,
            Money.FromDecimal(5m),
            "damaged",
            FixedUtcNow,
            CancellationToken.None);

        var refund = Assert.Single(payment.Refunds);
        Assert.NotNull(refund.ProviderRefundId);
        Assert.StartsWith("re_mock_", refund.ProviderRefundId);
        Assert.NotEqual("re_mock_", refund.ProviderRefundId);
    }

    [Fact]
    public async Task RefundAsync_WhenPaymentUsesMockProvider_LeavesProviderRefundIdNull()
    {
        var payment = CreateConfirmedPayment();

        var service = CreateService(providerName: "stripe");

        await service.RefundAsync(
            payment,
            Money.FromDecimal(5m),
            "damaged",
            FixedUtcNow,
            CancellationToken.None);

        var refund = Assert.Single(payment.Refunds);
        Assert.Null(refund.ProviderRefundId);
    }

    [Fact]
    public async Task RefundAsync_WithCustomProviderName_MatchesPaymentProvider()
    {
        var payment = CreateConfirmedPayment();
        payment.SetProvider("custom-provider");
        payment.SetProviderPaymentId("pp_123");

        var service = CreateService(providerName: "custom-provider");

        await service.RefundAsync(
            payment,
            Money.FromDecimal(5m),
            null,
            FixedUtcNow,
            CancellationToken.None);

        var refund = Assert.Single(payment.Refunds);
        Assert.StartsWith("re_mock_", refund.ProviderRefundId);
    }

    [Fact]
    public async Task RefundAsync_WhenAmountExceedsRemaining_ThrowsRefundNotAllowed()
    {
        var payment = CreateConfirmedPayment();

        var service = CreateService(providerName: "stripe");

        await Assert.ThrowsAsync<DomainConflictException>(
            () => service.RefundAsync(
                payment,
                Money.FromDecimal(101m),
                null,
                FixedUtcNow,
                CancellationToken.None));

        Assert.Empty(payment.Refunds);
    }

    private static PaymentRefundService CreateService(string providerName) =>
        new(Options.Create(new StripeOptions
        {
            Enabled = true,
            ProviderName = providerName,
            WebhookSecret = "whsec_test_secret_123456"
        }));

    private static Payment CreateConfirmedPayment()
    {
        var payment = Payment.Create(OrderId, Money.FromDecimal(100m), FixedUtcNow);
        payment.MarkConfirmed(FixedUtcNow);
        return payment;
    }
}

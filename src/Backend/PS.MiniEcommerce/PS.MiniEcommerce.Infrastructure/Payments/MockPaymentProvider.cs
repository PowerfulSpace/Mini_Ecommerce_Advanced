using PS.MiniEcommerce.Application.Abstractions.Payments;
using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Infrastructure.Payments;

public class MockPaymentProvider : IPaymentProvider
{
    private readonly object _sync = new();
    private readonly Queue<PaymentProviderResult> _scenario;

    public MockPaymentProvider()
        : this(Array.Empty<PaymentProviderResult>())
    {
    }

    public MockPaymentProvider(IEnumerable<PaymentProviderResult> scenario)
    {
        _scenario = new Queue<PaymentProviderResult>(scenario);
    }

    public void Enqueue(PaymentProviderResult result)
    {
        lock (_sync)
        {
            _scenario.Enqueue(result);
        }
    }

    public Task<PaymentProviderResult> CreatePaymentAsync(
        Guid orderId,
        Money amount,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(NextResult());
    }

    public Task<PaymentProviderResult> ConfirmAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(NextResult());
    }

    public Task<PaymentProviderResult> RefundAsync(
        Guid paymentId,
        Money amount,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(NextResult());
    }

    private PaymentProviderResult NextResult()
    {
        lock (_sync)
        {
            return _scenario.Count > 0
                ? _scenario.Dequeue()
                : PaymentProviderResult.Succeeded();
        }
    }
}

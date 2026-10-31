using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Domain.Payments;

public class Refund
{
    private const int MaxReasonLength = 500;

    public Guid Id { get; private set; }

    public Guid PaymentId { get; private set; }

    public Money Amount { get; private set; }

    public DateTime RefundedAtUtc { get; private set; }

    public string? Reason { get; private set; }

    public string? ProviderRefundId { get; private set; }

    private Refund()
    {
    }

    private Refund(Guid id, Guid paymentId, Money amount, string? reason, DateTime refundedAtUtc)
    {
        Id = id;
        PaymentId = paymentId;
        Amount = amount;
        Reason = reason;
        RefundedAtUtc = refundedAtUtc;
    }

    public static Refund Create(
        Guid paymentId,
        Money amount,
        string? reason,
        DateTime utcNow)
    {
        if (amount.Amount <= 0)
        {
            throw new DomainValidationException("Refund amount must be positive.");
        }

        if (reason is not null && reason.Length > MaxReasonLength)
        {
            throw new DomainValidationException("Refund reason must not exceed 500 characters.");
        }

        return new Refund(Guid.NewGuid(), paymentId, amount, reason, utcNow);
    }

    public void SetProviderRefundId(string providerRefundId)
    {
        if (string.IsNullOrWhiteSpace(providerRefundId))
        {
            throw new DomainValidationException("Provider refund id must not be empty.");
        }

        if (ProviderRefundId is not null)
        {
            throw new DomainConflictException(
                "Provider refund id has already been set.",
                "provider_refund_id_already_set");
        }

        ProviderRefundId = providerRefundId;
    }
}

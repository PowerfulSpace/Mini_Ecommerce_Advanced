using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Domain.Payments;

public class Payment
{
    private readonly List<Refund> _refunds = new();

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public Money Amount { get; private set; }

    public Money RefundedAmount { get; private set; }

    public PaymentStatus Status { get; private set; }

    public string Provider { get; private set; }

    public string? ProviderPaymentId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? ConfirmedAtUtc { get; private set; }

    public DateTime? RefundedAtUtc { get; private set; }

    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    private Payment()
    {
        Provider = string.Empty;
    }

    private Payment(
        Guid id,
        Guid orderId,
        Money amount,
        DateTime utcNow)
    {
        Id = id;
        OrderId = orderId;
        Amount = amount;
        RefundedAmount = Money.Zero;
        Status = PaymentStatus.Pending;
        Provider = "mock";
        ProviderPaymentId = "mock";
        CreatedAtUtc = utcNow;
    }

    public static Payment Create(Guid orderId, Money amount, DateTime utcNow)
    {
        if (amount.Amount < 0)
        {
            throw new DomainValidationException("Payment amount must not be negative.");
        }

        return new Payment(Guid.NewGuid(), orderId, amount, utcNow);
    }

    public void MarkConfirmed(DateTime utcNow)
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new DomainConflictException(
                "Only pending payments can be confirmed.",
                "payment_already_confirmed");
        }

        Status = PaymentStatus.Confirmed;
        ConfirmedAtUtc = utcNow;
    }

    public void MarkFailed()
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new DomainConflictException(
                "Only pending payments can fail.",
                "payment_failed");
        }

        Status = PaymentStatus.Failed;
    }

    public void AddRefund(Money amount, DateTime utcNow, string? reason = null)
    {
        if (amount.Amount <= 0)
        {
            throw new DomainValidationException("Refund amount must be positive.");
        }

        if ((RefundedAmount + amount).Amount > Amount.Amount)
        {
            throw new DomainConflictException(
                "Refund amount exceeds the remaining payment amount.",
                "refund_not_allowed");
        }

        var refund = Refund.Create(Id, amount, reason, utcNow);
        _refunds.Add(refund);
        RefundedAmount += amount;

        if (RefundedAmount == Amount)
        {
            Status = PaymentStatus.Refunded;
            RefundedAtUtc = utcNow;
        }
    }
}

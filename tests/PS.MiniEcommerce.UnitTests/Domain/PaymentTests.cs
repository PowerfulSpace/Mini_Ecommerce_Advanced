using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Exceptions;
using PS.MiniEcommerce.Domain.Payments;

namespace PS.MiniEcommerce.UnitTests.Domain;

public class PaymentTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid OrderId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Create_SetsPendingPaymentDefaults()
    {
        var payment = Payment.Create(OrderId, Money.FromDecimal(100m), FixedUtcNow);

        Assert.NotEqual(Guid.Empty, payment.Id);
        Assert.Equal(OrderId, payment.OrderId);
        Assert.Equal(100m, payment.Amount.Amount);
        Assert.Equal(0m, payment.RefundedAmount.Amount);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal("mock", payment.Provider);
        Assert.Equal("mock", payment.ProviderPaymentId);
        Assert.Equal(FixedUtcNow, payment.CreatedAtUtc);
        Assert.Null(payment.ConfirmedAtUtc);
        Assert.Null(payment.RefundedAtUtc);
        Assert.Empty(payment.Refunds);
    }

    [Fact]
    public void Create_WithNegativeAmount_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(
            () => Payment.Create(OrderId, Money.FromDecimal(-0.01m), FixedUtcNow));
    }

    [Fact]
    public void MarkConfirmed_FromPending_SetsStatusAndTimestamp()
    {
        var payment = Payment.Create(OrderId, Money.FromDecimal(100m), FixedUtcNow);

        payment.MarkConfirmed(FixedUtcNow.AddMinutes(1));

        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
        Assert.Equal(FixedUtcNow.AddMinutes(1), payment.ConfirmedAtUtc);
    }

    [Fact]
    public void MarkConfirmed_WhenAlreadyConfirmed_ThrowsPaymentAlreadyConfirmed()
    {
        var payment = Payment.Create(OrderId, Money.FromDecimal(100m), FixedUtcNow);
        payment.MarkConfirmed(FixedUtcNow);

        var exception = Assert.Throws<DomainConflictException>(
            () => payment.MarkConfirmed(FixedUtcNow));

        Assert.Equal("payment_already_confirmed", exception.Code);
    }

    [Fact]
    public void MarkFailed_FromPending_SetsFailed()
    {
        var payment = Payment.Create(OrderId, Money.FromDecimal(100m), FixedUtcNow);

        payment.MarkFailed();

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Null(payment.ConfirmedAtUtc);
    }

    [Fact]
    public void MarkFailed_WhenAlreadyConfirmed_ThrowsPaymentFailed()
    {
        var payment = Payment.Create(OrderId, Money.FromDecimal(100m), FixedUtcNow);
        payment.MarkConfirmed(FixedUtcNow);

        var exception = Assert.Throws<DomainConflictException>(payment.MarkFailed);

        Assert.Equal("payment_failed", exception.Code);
    }

    [Fact]
    public void AddRefund_Partial_AccumulatesRefundAndKeepsConfirmed()
    {
        var payment = CreateConfirmedPayment(100m);

        payment.AddRefund(Money.FromDecimal(25m), FixedUtcNow.AddMinutes(2), "damaged");

        Assert.Equal(25m, payment.RefundedAmount.Amount);
        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
        Assert.Null(payment.RefundedAtUtc);
        var refund = Assert.Single(payment.Refunds);
        Assert.Equal(25m, refund.Amount.Amount);
        Assert.Equal("damaged", refund.Reason);
        Assert.Equal(payment.Id, refund.PaymentId);
    }

    [Fact]
    public void AddRefund_MultiplePartials_AccumulatesRefundedAmount()
    {
        var payment = CreateConfirmedPayment(100m);

        payment.AddRefund(Money.FromDecimal(30m), FixedUtcNow);
        payment.AddRefund(Money.FromDecimal(20m), FixedUtcNow);

        Assert.Equal(50m, payment.RefundedAmount.Amount);
        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
        Assert.Equal(2, payment.Refunds.Count);
    }

    [Fact]
    public void AddRefund_FullAmount_SetsRefundedStatusAndTimestamp()
    {
        var payment = CreateConfirmedPayment(100m);
        var refundedAt = FixedUtcNow.AddMinutes(5);

        payment.AddRefund(Money.FromDecimal(100m), refundedAt);

        Assert.Equal(100m, payment.RefundedAmount.Amount);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(refundedAt, payment.RefundedAtUtc);
    }

    [Fact]
    public void AddRefund_AfterPartial_WhenRemainingAmountRefunded_SetsRefundedStatus()
    {
        var payment = CreateConfirmedPayment(100m);
        payment.AddRefund(Money.FromDecimal(40m), FixedUtcNow);

        payment.AddRefund(Money.FromDecimal(60m), FixedUtcNow.AddMinutes(1));

        Assert.Equal(100m, payment.RefundedAmount.Amount);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(FixedUtcNow.AddMinutes(1), payment.RefundedAtUtc);
    }

    [Fact]
    public void AddRefund_ExceedingRemainingAmount_ThrowsRefundNotAllowed()
    {
        var payment = CreateConfirmedPayment(100m);
        payment.AddRefund(Money.FromDecimal(60m), FixedUtcNow);

        var exception = Assert.Throws<DomainConflictException>(
            () => payment.AddRefund(Money.FromDecimal(40.01m), FixedUtcNow));

        Assert.Equal("refund_not_allowed", exception.Code);
        Assert.Equal(60m, payment.RefundedAmount.Amount);
        Assert.Single(payment.Refunds);
    }

    [Fact]
    public void AddRefund_AfterFullRefund_ThrowsRefundNotAllowed()
    {
        var payment = CreateConfirmedPayment(100m);
        payment.AddRefund(Money.FromDecimal(100m), FixedUtcNow);

        var exception = Assert.Throws<DomainConflictException>(
            () => payment.AddRefund(Money.FromDecimal(1m), FixedUtcNow));

        Assert.Equal("refund_not_allowed", exception.Code);
    }

    [Theory]
    [InlineData(PaymentStatus.Pending)]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Refunded)]
    public void AddRefund_WhenPaymentIsNotConfirmed_ThrowsRefundNotAllowed(PaymentStatus status)
    {
        var payment = Payment.Create(OrderId, Money.FromDecimal(100m), FixedUtcNow);

        if (status == PaymentStatus.Failed)
        {
            payment.MarkFailed();
        }
        else if (status == PaymentStatus.Refunded)
        {
            payment.MarkConfirmed(FixedUtcNow);
            payment.AddRefund(Money.FromDecimal(100m), FixedUtcNow);
        }

        var exception = Assert.Throws<DomainConflictException>(
            () => payment.AddRefund(Money.FromDecimal(1m), FixedUtcNow));

        Assert.Equal("refund_not_allowed", exception.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddRefund_WithNonPositiveAmount_ThrowsDomainValidationException(int amount)
    {
        var payment = CreateConfirmedPayment(100m);

        Assert.Throws<DomainValidationException>(
            () => payment.AddRefund(Money.FromDecimal(amount), FixedUtcNow));
    }

    private static Payment CreateConfirmedPayment(decimal amount)
    {
        var payment = Payment.Create(OrderId, Money.FromDecimal(amount), FixedUtcNow);
        payment.MarkConfirmed(FixedUtcNow);
        return payment;
    }
}

using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Exceptions;
using PS.MiniEcommerce.Domain.Payments;

namespace PS.MiniEcommerce.UnitTests.Domain;

public class RefundTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid PaymentId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Create_SetsProperties()
    {
        var refund = Refund.Create(PaymentId, Money.FromDecimal(25m), "damaged", FixedUtcNow);

        Assert.NotEqual(Guid.Empty, refund.Id);
        Assert.Equal(PaymentId, refund.PaymentId);
        Assert.Equal(25m, refund.Amount.Amount);
        Assert.Equal("damaged", refund.Reason);
        Assert.Equal(FixedUtcNow, refund.RefundedAtUtc);
    }

    [Fact]
    public void Create_WithNullReason_AllowsNull()
    {
        var refund = Refund.Create(PaymentId, Money.FromDecimal(25m), null, FixedUtcNow);

        Assert.Null(refund.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_WithNonPositiveAmount_ThrowsDomainValidationException(int amount)
    {
        Assert.Throws<DomainValidationException>(
            () => Refund.Create(PaymentId, Money.FromDecimal(amount), null, FixedUtcNow));
    }

    [Fact]
    public void Create_WithReasonLongerThan500Characters_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(
            () => Refund.Create(PaymentId, Money.FromDecimal(25m), new string('a', 501), FixedUtcNow));
    }
}

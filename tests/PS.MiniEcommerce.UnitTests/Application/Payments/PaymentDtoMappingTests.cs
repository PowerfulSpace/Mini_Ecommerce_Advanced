using PS.MiniEcommerce.Application.Payments.DTOs;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Payments;

namespace PS.MiniEcommerce.UnitTests.Application.Payments;

public class PaymentDtoMappingTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid OrderId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void ToDto_MapsPaymentAndLeavesClientSecretNull()
    {
        var payment = Payment.Create(OrderId, Money.FromDecimal(100m), FixedUtcNow);

        var dto = payment.ToDto();

        Assert.Equal(payment.Id, dto.Id);
        Assert.Equal(payment.OrderId, dto.OrderId);
        Assert.Equal(100m, dto.Amount);
        Assert.Equal(0m, dto.RefundedAmount);
        Assert.Equal("Pending", dto.Status);
        Assert.Equal(FixedUtcNow, dto.CreatedAtUtc);
        Assert.Null(dto.ConfirmedAtUtc);
        Assert.Null(dto.RefundedAtUtc);
        Assert.Null(dto.ClientSecret);
    }
}

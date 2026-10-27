using PS.MiniEcommerce.Application.Orders.Commands;

namespace PS.MiniEcommerce.UnitTests.Application.Orders;

public class CancelOrderValidatorTests
{
    private const string ValidIfMatch = "\"AQIDBAUGBwg=\"";
    private static readonly Guid OrderId = Guid.NewGuid();
    private static readonly Guid OrderItemId = Guid.NewGuid();

    private readonly CancelOrderValidator _validator = new();

    [Fact]
    public void Validate_WithFullCancellation_IsValid()
    {
        var result = _validator.Validate(new CancelOrderCommand(OrderId, ValidIfMatch));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithPartialCancellation_IsValid()
    {
        var result = _validator.Validate(
            new CancelOrderCommand(
                OrderId,
                ValidIfMatch,
                new[] { new CancelOrderItemDto(OrderItemId, 1) }));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithEmptyOrderId_IsInvalid()
    {
        var result = _validator.Validate(new CancelOrderCommand(Guid.Empty, ValidIfMatch));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "OrderId");
    }

    [Fact]
    public void Validate_WithEmptyItems_IsInvalid()
    {
        var result = _validator.Validate(
            new CancelOrderCommand(OrderId, ValidIfMatch, Array.Empty<CancelOrderItemDto>()));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "Items");
    }

    [Fact]
    public void Validate_WithEmptyOrderItemId_IsInvalid()
    {
        var result = _validator.Validate(
            new CancelOrderCommand(
                OrderId,
                ValidIfMatch,
                new[] { new CancelOrderItemDto(Guid.Empty, 1) }));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName.EndsWith("OrderItemId"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_WithNonPositiveQuantity_IsInvalid(int quantity)
    {
        var result = _validator.Validate(
            new CancelOrderCommand(
                OrderId,
                ValidIfMatch,
                new[] { new CancelOrderItemDto(OrderItemId, quantity) }));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName.EndsWith("Quantity"));
    }

    [Fact]
    public void Validate_WithDuplicateOrderItemIds_IsInvalid()
    {
        var result = _validator.Validate(
            new CancelOrderCommand(
                OrderId,
                ValidIfMatch,
                new[]
                {
                    new CancelOrderItemDto(OrderItemId, 1),
                    new CancelOrderItemDto(OrderItemId, 1)
                }));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "Items");
    }
}

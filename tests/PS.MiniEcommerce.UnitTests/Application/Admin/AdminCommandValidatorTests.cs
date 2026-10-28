using PS.MiniEcommerce.Application.Admin.Commands;

namespace PS.MiniEcommerce.UnitTests.Application.Admin;

public class AdminCommandValidatorTests
{
    [Fact]
    public void StockValidator_WithNegativeAvailable_IsInvalid()
    {
        var validator = new AdminUpdateProductStockValidator();

        var result = validator.Validate(new AdminUpdateProductStockCommand(Guid.NewGuid(), -1));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "Available");
    }

    [Fact]
    public void StockValidator_WithEmptyProductId_IsInvalid()
    {
        var validator = new AdminUpdateProductStockValidator();

        var result = validator.Validate(new AdminUpdateProductStockCommand(Guid.Empty, 5));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "ProductId");
    }

    [Fact]
    public void DeactivateValidator_WithEmptyProductId_IsInvalid()
    {
        var validator = new AdminDeactivateProductValidator();

        var result = validator.Validate(new AdminDeactivateProductCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "ProductId");
    }

    [Fact]
    public void ShipValidator_WithEmptyOrderId_IsInvalid()
    {
        var validator = new AdminShipOrderValidator();

        var result = validator.Validate(new AdminShipOrderCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "OrderId");
    }

    [Fact]
    public void DeliverValidator_WithEmptyOrderId_IsInvalid()
    {
        var validator = new AdminDeliverOrderValidator();

        var result = validator.Validate(new AdminDeliverOrderCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "OrderId");
    }

    [Fact]
    public void ReturnValidator_WithEmptyOrderId_IsInvalid()
    {
        var validator = new AdminReturnOrderValidator();

        var result = validator.Validate(new AdminReturnOrderCommand(Guid.Empty));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "OrderId");
    }
}

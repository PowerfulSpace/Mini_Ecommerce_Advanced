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
}

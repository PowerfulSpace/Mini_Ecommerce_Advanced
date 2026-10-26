using PS.MiniEcommerce.Application.Carts.Commands;

namespace PS.MiniEcommerce.UnitTests.Application.Carts;

public class AddCartItemValidatorTests
{
    private readonly AddCartItemValidator _validator = new();

    [Fact]
    public void Validate_WithQuantityZero_IsInvalid()
    {
        var result = _validator.Validate(new AddCartItemCommand(Guid.NewGuid(), 0, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "Quantity");
    }

    [Fact]
    public void Validate_WithNegativeQuantity_IsInvalid()
    {
        var result = _validator.Validate(new AddCartItemCommand(Guid.NewGuid(), -1, null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "Quantity");
    }

    [Fact]
    public void Validate_WithPositiveQuantity_IsValid()
    {
        var result = _validator.Validate(new AddCartItemCommand(Guid.NewGuid(), 1, null));

        Assert.True(result.IsValid);
    }
}

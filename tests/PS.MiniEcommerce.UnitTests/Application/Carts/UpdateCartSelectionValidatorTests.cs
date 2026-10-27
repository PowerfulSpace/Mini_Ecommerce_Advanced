using PS.MiniEcommerce.Application.Carts.Commands;

namespace PS.MiniEcommerce.UnitTests.Application.Carts;

public class UpdateCartSelectionValidatorTests
{
    private readonly UpdateCartSelectionValidator _validator = new();

    [Fact]
    public void Validate_WithEmptyItems_IsInvalid()
    {
        var result = _validator.Validate(
            new UpdateCartSelectionCommand(Array.Empty<CartItemSelectionDto>(), null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "Items");
    }

    [Fact]
    public void Validate_WithEmptyProductId_IsInvalid()
    {
        var result = _validator.Validate(
            new UpdateCartSelectionCommand(
                new[] { new CartItemSelectionDto(Guid.Empty, true) },
                null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName.EndsWith("ProductId"));
    }

    [Fact]
    public void Validate_WithDuplicateProductIds_IsInvalid()
    {
        var productId = Guid.NewGuid();
        var result = _validator.Validate(
            new UpdateCartSelectionCommand(
                new[]
                {
                    new CartItemSelectionDto(productId, true),
                    new CartItemSelectionDto(productId, false)
                },
                null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "Items");
    }

    [Fact]
    public void Validate_WithValidItems_IsValid()
    {
        var result = _validator.Validate(
            new UpdateCartSelectionCommand(
                new[]
                {
                    new CartItemSelectionDto(Guid.NewGuid(), true),
                    new CartItemSelectionDto(Guid.NewGuid(), false)
                },
                null));

        Assert.True(result.IsValid);
    }
}

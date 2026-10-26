using PS.MiniEcommerce.Application.Products.Queries;

namespace PS.MiniEcommerce.UnitTests.Application.Products;

public class GetProductsQueryValidatorTests
{
    private readonly GetProductsQueryValidator _validator = new();

    [Fact]
    public void Validate_WithPageZero_IsInvalid()
    {
        var result = _validator.Validate(new GetProductsQuery(0, 20));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Page");
        Assert.Contains(
            result.Errors,
            e => e.ErrorMessage.Contains("greater than or equal to", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WithPageOne_IsValid()
    {
        var result = _validator.Validate(new GetProductsQuery(1, 20));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithPageSizeZero_IsInvalid()
    {
        var result = _validator.Validate(new GetProductsQuery(1, 0));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "PageSize");
    }

    [Fact]
    public void Validate_WithPageSize101_IsInvalid()
    {
        var result = _validator.Validate(new GetProductsQuery(1, 101));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "PageSize");
    }

    [Fact]
    public void Validate_WithPageSize100_IsValid()
    {
        var result = _validator.Validate(new GetProductsQuery(1, 100));

        Assert.True(result.IsValid);
    }
}

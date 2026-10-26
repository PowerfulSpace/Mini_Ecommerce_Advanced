using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.UnitTests.Domain;

public class ProductTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private static Product CreateValidProduct(int stockQuantity = 10, decimal price = 9.99m)
    {
        return Product.Create("Sample product", "A sample product.", price, stockQuantity, FixedUtcNow);
    }

    [Fact]
    public void Create_WithValidData_SetsProperties()
    {
        var product = Product.Create("Sample product", "A sample product.", 9.99m, 10, FixedUtcNow);

        Assert.NotEqual(Guid.Empty, product.Id);
        Assert.Equal("Sample product", product.Name);
        Assert.Equal("A sample product.", product.Description);
        Assert.Equal(9.99m, product.Price);
        Assert.Equal(10, product.StockQuantity);
        Assert.True(product.IsActive);
        Assert.Equal(FixedUtcNow, product.CreatedAtUtc);
    }

    [Fact]
    public void Create_WithEmptyName_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(
            () => Product.Create(string.Empty, "A sample product.", 9.99m, 10, FixedUtcNow));
    }

    [Fact]
    public void Create_WithWhitespaceName_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(
            () => Product.Create("   ", "A sample product.", 9.99m, 10, FixedUtcNow));
    }

    [Fact]
    public void Create_WithNullName_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(
            () => Product.Create(null!, "A sample product.", 9.99m, 10, FixedUtcNow));
    }

    [Fact]
    public void Create_WithNameLongerThan200Characters_ThrowsDomainValidationException()
    {
        var name = new string('a', 201);

        Assert.Throws<DomainValidationException>(
            () => Product.Create(name, "A sample product.", 9.99m, 10, FixedUtcNow));
    }

    [Fact]
    public void Create_WithNullDescription_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(
            () => Product.Create("Sample product", null!, 9.99m, 10, FixedUtcNow));
    }

    [Fact]
    public void Create_WithDescriptionLongerThan2000Characters_ThrowsDomainValidationException()
    {
        var description = new string('a', 2001);

        Assert.Throws<DomainValidationException>(
            () => Product.Create("Sample product", description, 9.99m, 10, FixedUtcNow));
    }

    [Fact]
    public void Create_WithNegativePrice_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(
            () => Product.Create("Sample product", "A sample product.", -0.01m, 10, FixedUtcNow));
    }

    [Fact]
    public void Create_WithNegativeStockQuantity_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(
            () => Product.Create("Sample product", "A sample product.", 9.99m, -1, FixedUtcNow));
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var product = CreateValidProduct();

        product.Deactivate();

        Assert.False(product.IsActive);
    }

    [Fact]
    public void Deactivate_WhenAlreadyInactive_DoesNotThrow()
    {
        var product = CreateValidProduct();

        product.Deactivate();
        product.Deactivate();

        Assert.False(product.IsActive);
    }

    [Fact]
    public void DeductStock_WithValidQuantity_DecreasesStockQuantity()
    {
        var product = CreateValidProduct(stockQuantity: 10);

        product.DeductStock(4);

        Assert.Equal(6, product.StockQuantity);
    }

    [Fact]
    public void DeductStock_WithZeroQuantity_ThrowsDomainValidationException()
    {
        var product = CreateValidProduct();

        Assert.Throws<DomainValidationException>(() => product.DeductStock(0));
    }

    [Fact]
    public void DeductStock_WithNegativeQuantity_ThrowsDomainValidationException()
    {
        var product = CreateValidProduct();

        Assert.Throws<DomainValidationException>(() => product.DeductStock(-1));
    }

    [Fact]
    public void DeductStock_WithQuantityGreaterThanStock_ThrowsDomainConflictException()
    {
        var product = CreateValidProduct(stockQuantity: 5);

        Assert.Throws<DomainConflictException>(() => product.DeductStock(6));
    }

    [Fact]
    public void DeductStock_WithQuantityEqualToStock_DecreasesStockToZero()
    {
        var product = CreateValidProduct(stockQuantity: 5);

        product.DeductStock(5);

        Assert.Equal(0, product.StockQuantity);
    }

    [Fact]
    public void RestoreStock_WithValidQuantity_IncreasesStockQuantity()
    {
        var product = CreateValidProduct(stockQuantity: 10);

        product.RestoreStock(3);

        Assert.Equal(13, product.StockQuantity);
    }

    [Fact]
    public void RestoreStock_WithZeroQuantity_ThrowsDomainValidationException()
    {
        var product = CreateValidProduct();

        Assert.Throws<DomainValidationException>(() => product.RestoreStock(0));
    }

    [Fact]
    public void RestoreStock_WithNegativeQuantity_ThrowsDomainValidationException()
    {
        var product = CreateValidProduct();

        Assert.Throws<DomainValidationException>(() => product.RestoreStock(-1));
    }
}

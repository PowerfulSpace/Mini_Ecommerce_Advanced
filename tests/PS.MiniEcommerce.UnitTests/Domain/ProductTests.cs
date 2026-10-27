using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.UnitTests.Domain;

public class ProductTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private static Product CreateValidProduct(int available = 10, int reserved = 0, decimal price = 9.99m)
    {
        return Product.Create(
            "Sample product",
            "A sample product.",
            Money.FromDecimal(price),
            available,
            FixedUtcNow,
            reserved);
    }

    [Fact]
    public void Create_WithValidData_SetsProperties()
    {
        var product = Product.Create(
            "Sample product",
            "A sample product.",
            Money.FromDecimal(9.99m),
            10,
            FixedUtcNow);

        Assert.NotEqual(Guid.Empty, product.Id);
        Assert.Equal("Sample product", product.Name);
        Assert.Equal("A sample product.", product.Description);
        Assert.Equal(9.99m, product.Price.Amount);
        Assert.Equal(10, product.Available);
        Assert.Equal(0, product.Reserved);
        Assert.True(product.IsActive);
        Assert.Equal(FixedUtcNow, product.CreatedAtUtc);
    }

    [Fact]
    public void Create_WithReservedQuantity_SetsReserved()
    {
        var product = CreateValidProduct(available: 10, reserved: 4);

        Assert.Equal(10, product.Available);
        Assert.Equal(4, product.Reserved);
    }

    [Fact]
    public void Create_WithEmptyName_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(
            () => Product.Create(string.Empty, "A sample product.", Money.FromDecimal(9.99m), 10, FixedUtcNow));
    }

    [Fact]
    public void Create_WithWhitespaceName_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(
            () => Product.Create("   ", "A sample product.", Money.FromDecimal(9.99m), 10, FixedUtcNow));
    }

    [Fact]
    public void Create_WithNullName_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(
            () => Product.Create(null!, "A sample product.", Money.FromDecimal(9.99m), 10, FixedUtcNow));
    }

    [Fact]
    public void Create_WithNameLongerThan200Characters_ThrowsDomainValidationException()
    {
        var name = new string('a', 201);

        Assert.Throws<DomainValidationException>(
            () => Product.Create(name, "A sample product.", Money.FromDecimal(9.99m), 10, FixedUtcNow));
    }

    [Fact]
    public void Create_WithNullDescription_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(
            () => Product.Create("Sample product", null!, Money.FromDecimal(9.99m), 10, FixedUtcNow));
    }

    [Fact]
    public void Create_WithDescriptionLongerThan2000Characters_ThrowsDomainValidationException()
    {
        var description = new string('a', 2001);

        Assert.Throws<DomainValidationException>(
            () => Product.Create("Sample product", description, Money.FromDecimal(9.99m), 10, FixedUtcNow));
    }

    [Fact]
    public void Create_WithNegativePrice_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(
            () => Product.Create("Sample product", "A sample product.", Money.FromDecimal(-0.01m), 10, FixedUtcNow));
    }

    [Fact]
    public void Create_WithNegativeAvailable_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(
            () => Product.Create("Sample product", "A sample product.", Money.FromDecimal(9.99m), -1, FixedUtcNow));
    }

    [Fact]
    public void Create_WithNegativeReserved_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(
            () => Product.Create(
                "Sample product",
                "A sample product.",
                Money.FromDecimal(9.99m),
                10,
                FixedUtcNow,
                reserved: -1));
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
    public void Reserve_WithValidQuantity_MovesAvailableToReserved()
    {
        var product = CreateValidProduct(available: 10);

        product.Reserve(4);

        Assert.Equal(6, product.Available);
        Assert.Equal(4, product.Reserved);
    }

    [Fact]
    public void Reserve_WithZeroQuantity_ThrowsDomainValidationException()
    {
        var product = CreateValidProduct();

        Assert.Throws<DomainValidationException>(() => product.Reserve(0));
    }

    [Fact]
    public void Reserve_WithNegativeQuantity_ThrowsDomainValidationException()
    {
        var product = CreateValidProduct();

        Assert.Throws<DomainValidationException>(() => product.Reserve(-1));
    }

    [Fact]
    public void Reserve_WithQuantityGreaterThanAvailable_ThrowsDomainConflictException()
    {
        var product = CreateValidProduct(available: 5);

        Assert.Throws<DomainConflictException>(() => product.Reserve(6));
    }

    [Fact]
    public void Reserve_WithQuantityEqualToAvailable_MovesAllStockToReserved()
    {
        var product = CreateValidProduct(available: 5);

        product.Reserve(5);

        Assert.Equal(0, product.Available);
        Assert.Equal(5, product.Reserved);
    }

    [Fact]
    public void Release_WithValidQuantity_MovesReservedToAvailable()
    {
        var product = CreateValidProduct(available: 10, reserved: 4);

        product.Release(4);

        Assert.Equal(14, product.Available);
        Assert.Equal(0, product.Reserved);
    }

    [Fact]
    public void Release_WithZeroQuantity_ThrowsDomainValidationException()
    {
        var product = CreateValidProduct();

        Assert.Throws<DomainValidationException>(() => product.Release(0));
    }

    [Fact]
    public void Release_WithNegativeQuantity_ThrowsDomainValidationException()
    {
        var product = CreateValidProduct();

        Assert.Throws<DomainValidationException>(() => product.Release(-1));
    }

    [Fact]
    public void Release_WithQuantityGreaterThanReserved_ThrowsDomainConflictException()
    {
        var product = CreateValidProduct(available: 10, reserved: 3);

        Assert.Throws<DomainConflictException>(() => product.Release(4));
    }

    [Fact]
    public void Consume_WithValidQuantity_DecreasesReserved()
    {
        var product = CreateValidProduct(available: 10, reserved: 4);

        product.Consume(3);

        Assert.Equal(10, product.Available);
        Assert.Equal(1, product.Reserved);
    }

    [Fact]
    public void Consume_WithZeroQuantity_ThrowsDomainValidationException()
    {
        var product = CreateValidProduct();

        Assert.Throws<DomainValidationException>(() => product.Consume(0));
    }

    [Fact]
    public void Consume_WithNegativeQuantity_ThrowsDomainValidationException()
    {
        var product = CreateValidProduct();

        Assert.Throws<DomainValidationException>(() => product.Consume(-1));
    }

    [Fact]
    public void Consume_WithQuantityGreaterThanReserved_ThrowsDomainConflictException()
    {
        var product = CreateValidProduct(available: 10, reserved: 3);

        Assert.Throws<DomainConflictException>(() => product.Consume(4));
    }

    [Fact]
    public void RestoreAvailable_WithValidQuantity_IncreasesAvailable()
    {
        var product = CreateValidProduct(available: 10, reserved: 4);

        product.RestoreAvailable(3);

        Assert.Equal(13, product.Available);
        Assert.Equal(4, product.Reserved);
    }

    [Fact]
    public void RestoreAvailable_WithZeroQuantity_ThrowsDomainValidationException()
    {
        var product = CreateValidProduct();

        Assert.Throws<DomainValidationException>(() => product.RestoreAvailable(0));
    }

    [Fact]
    public void RestoreAvailable_WithNegativeQuantity_ThrowsDomainValidationException()
    {
        var product = CreateValidProduct();

        Assert.Throws<DomainValidationException>(() => product.RestoreAvailable(-1));
    }

    [Fact]
    public void AdjustAvailable_WithValidValue_SetsAvailableAndKeepsReserved()
    {
        var product = CreateValidProduct(available: 10, reserved: 4);

        product.AdjustAvailable(5);

        Assert.Equal(5, product.Available);
        Assert.Equal(4, product.Reserved);
    }

    [Fact]
    public void AdjustAvailable_WithNegativeValue_ThrowsDomainValidationException()
    {
        var product = CreateValidProduct();

        Assert.Throws<DomainValidationException>(() => product.AdjustAvailable(-1));
    }
}

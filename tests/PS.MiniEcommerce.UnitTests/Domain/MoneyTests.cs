using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.UnitTests.Domain;

public class MoneyTests
{
    [Fact]
    public void FromDecimal_RoundsToTwoDecimalsAwayFromZero()
    {
        var money = Money.FromDecimal(1.005m);

        Assert.Equal(1.01m, money.Amount);
    }

    [Fact]
    public void FromDecimal_RoundsDownWhenBelowMidpoint()
    {
        var money = Money.FromDecimal(1.004m);

        Assert.Equal(1.00m, money.Amount);
    }

    [Fact]
    public void FromDecimal_AllowsNegativeAmounts()
    {
        var money = Money.FromDecimal(-1.23m);

        Assert.Equal(-1.23m, money.Amount);
    }

    [Fact]
    public void Zero_HasZeroAmount()
    {
        Assert.Equal(0m, Money.Zero.Amount);
    }

    [Fact]
    public void Addition_ReturnsSum()
    {
        var result = Money.FromDecimal(1.10m) + Money.FromDecimal(2.20m);

        Assert.Equal(3.30m, result.Amount);
    }

    [Fact]
    public void Subtraction_ReturnsDifference()
    {
        var result = Money.FromDecimal(5.00m) - Money.FromDecimal(2.25m);

        Assert.Equal(2.75m, result.Amount);
    }

    [Fact]
    public void Subtraction_CanProduceNegativeAmount()
    {
        var result = Money.FromDecimal(2.00m) - Money.FromDecimal(5.00m);

        Assert.Equal(-3.00m, result.Amount);
    }

    [Fact]
    public void Multiplication_ByQuantityReturnsProduct()
    {
        var result = Money.FromDecimal(19.99m) * 3;

        Assert.Equal(59.97m, result.Amount);
    }

    [Fact]
    public void Multiplication_ResultIsRounded()
    {
        var result = Money.FromDecimal(1.005m) * 1;

        Assert.Equal(1.01m, result.Amount);
    }

    [Fact]
    public void Equality_ComparesByValue()
    {
        Assert.Equal(Money.FromDecimal(9.99m), Money.FromDecimal(9.99m));
        Assert.NotEqual(Money.FromDecimal(9.99m), Money.FromDecimal(9.98m));
    }

    [Fact]
    public void Currency_IsUsd()
    {
        Assert.Equal("USD", Money.Currency);
    }
}

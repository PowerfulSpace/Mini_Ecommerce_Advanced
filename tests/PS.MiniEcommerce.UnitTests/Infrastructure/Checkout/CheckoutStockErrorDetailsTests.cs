using PS.MiniEcommerce.Infrastructure.Checkout;

namespace PS.MiniEcommerce.UnitTests.Infrastructure.Checkout;

public class CheckoutStockErrorDetailsTests
{
    private static readonly AffectedProduct First =
        new(Guid.Parse("00000000-0000-0000-0000-000000000001"), "First product");

    private static readonly AffectedProduct Second =
        new(Guid.Parse("00000000-0000-0000-0000-000000000002"), "Second product");

    [Fact]
    public void FormatInsufficientStock_OrdersProductsByProductIdAndUsesRequiredFormat()
    {
        var detail = CheckoutStockErrorDetails.FormatInsufficientStock([Second, First]);

        Assert.Equal(
            $"Insufficient stock for: {First.Name} ({First.Id}), {Second.Name} ({Second.Id})",
            detail);
    }

    [Fact]
    public void FormatUnavailableProducts_OrdersProductsByProductIdAndUsesRequiredFormat()
    {
        var detail = CheckoutStockErrorDetails.FormatUnavailableProducts([Second, First]);

        Assert.Equal(
            $"Unavailable products: {First.Name} ({First.Id}), {Second.Name} ({Second.Id})",
            detail);
    }
}

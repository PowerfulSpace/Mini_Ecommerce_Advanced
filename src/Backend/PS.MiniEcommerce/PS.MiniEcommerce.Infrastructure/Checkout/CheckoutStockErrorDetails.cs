namespace PS.MiniEcommerce.Infrastructure.Checkout;

internal readonly record struct AffectedProduct(Guid Id, string Name);

internal static class CheckoutStockErrorDetails
{
    public static string FormatInsufficientStock(IEnumerable<AffectedProduct> products) =>
        $"Insufficient stock for: {FormatProducts(products)}";

    public static string FormatUnavailableProducts(IEnumerable<AffectedProduct> products) =>
        $"Unavailable products: {FormatProducts(products)}";

    private static string FormatProducts(IEnumerable<AffectedProduct> products) =>
        string.Join(", ", products
            .OrderBy(product => product.Id)
            .Select(product => $"{product.Name} ({product.Id})"));
}

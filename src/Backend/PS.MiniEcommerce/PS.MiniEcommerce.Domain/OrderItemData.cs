namespace PS.MiniEcommerce.Domain;

public sealed record OrderItemData(
    Guid ProductId,
    string ProductName,
    Money UnitPrice,
    int Quantity);

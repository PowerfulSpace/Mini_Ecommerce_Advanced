namespace PS.MiniEcommerce.Domain;

public sealed record OrderItemData(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity);

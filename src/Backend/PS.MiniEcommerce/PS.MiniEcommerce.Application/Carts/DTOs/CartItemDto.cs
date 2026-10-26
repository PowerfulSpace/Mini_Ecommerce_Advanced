namespace PS.MiniEcommerce.Application.Carts.DTOs;

public sealed record CartItemDto(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    bool IsAvailable);

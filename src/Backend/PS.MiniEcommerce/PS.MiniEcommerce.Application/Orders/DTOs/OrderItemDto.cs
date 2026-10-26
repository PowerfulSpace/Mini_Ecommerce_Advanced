namespace PS.MiniEcommerce.Application.Orders.DTOs;

public sealed record OrderItemDto(
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

namespace PS.MiniEcommerce.Application.Orders.DTOs;

public sealed record OrderItemDto(
    Guid OrderItemId,
    Guid ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    int CancelledQuantity,
    decimal LineTotal);

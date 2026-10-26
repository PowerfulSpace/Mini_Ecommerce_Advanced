namespace PS.MiniEcommerce.Application.Orders.DTOs;

public sealed record OrderDto(
    Guid Id,
    string Status,
    DateTime CreatedAtUtc,
    decimal TotalAmount,
    IReadOnlyList<OrderItemDto> Items);

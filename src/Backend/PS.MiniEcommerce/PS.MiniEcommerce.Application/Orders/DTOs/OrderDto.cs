using PS.MiniEcommerce.Application.Payments.DTOs;

namespace PS.MiniEcommerce.Application.Orders.DTOs;

public sealed record OrderDto(
    Guid Id,
    string Status,
    DateTime CreatedAtUtc,
    decimal TotalAmount,
    Guid? PaymentId,
    IReadOnlyList<OrderItemDto> Items,
    PaymentDto? Payment);

namespace PS.MiniEcommerce.Application.Orders.DTOs;

public sealed record OrderSummaryDto(
    Guid Id,
    string Status,
    DateTime CreatedAtUtc,
    decimal TotalAmount);

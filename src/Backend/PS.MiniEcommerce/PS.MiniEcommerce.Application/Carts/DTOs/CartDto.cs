namespace PS.MiniEcommerce.Application.Carts.DTOs;

public sealed record CartDto(
    IReadOnlyList<CartItemDto> Items,
    decimal TotalAmount);

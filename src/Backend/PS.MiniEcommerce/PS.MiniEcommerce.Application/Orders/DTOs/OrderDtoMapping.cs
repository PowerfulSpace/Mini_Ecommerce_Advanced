using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Application.Orders.DTOs;

public static class OrderDtoMapping
{
    public static OrderDto ToDto(this Order order) => new(
        order.Id,
        order.Status.ToString(),
        order.CreatedAtUtc,
        order.TotalAmount,
        order.Items
            .Select(item => new OrderItemDto(
                item.ProductId,
                item.ProductName,
                item.UnitPrice,
                item.Quantity,
                item.UnitPrice * item.Quantity))
            .ToList());

    public static OrderSummaryDto ToSummaryDto(this Order order) => new(
        order.Id,
        order.Status.ToString(),
        order.CreatedAtUtc,
        order.TotalAmount);
}

using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Application.Orders.DTOs;

public static class OrderDtoMapping
{
    public static OrderDto ToDto(this Order order) => new(
        order.Id,
        order.Status.ToString(),
        order.CreatedAtUtc,
        order.TotalAmount.Amount,
        order.Items
            .Select(item => new OrderItemDto(
                item.ProductId,
                item.ProductName,
                item.UnitPrice.Amount,
                item.Quantity,
                (item.UnitPrice * item.Quantity).Amount))
            .ToList());

    public static OrderSummaryDto ToSummaryDto(this Order order) => new(
        order.Id,
        order.Status.ToString(),
        order.CreatedAtUtc,
        order.TotalAmount.Amount);
}

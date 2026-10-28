using PS.MiniEcommerce.Application.Payments.DTOs;
using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Application.Orders.DTOs;

public static class OrderDtoMapping
{
    public static OrderDto ToDto(this Order order) => new(
        order.Id,
        order.Status.ToString(),
        order.CreatedAtUtc,
        order.TotalAmount.Amount,
        order.PaymentId,
        order.Items
            .Select(item => new OrderItemDto(
                item.Id,
                item.ProductId,
                item.ProductName,
                item.UnitPrice.Amount,
                item.Quantity,
                item.CancelledQuantity,
                (item.UnitPrice * item.Quantity).Amount))
            .ToList(),
        order.Payment?.ToDto());

    public static OrderSummaryDto ToSummaryDto(this Order order) => new(
        order.Id,
        order.Status.ToString(),
        order.CreatedAtUtc,
        order.TotalAmount.Amount,
        order.PaymentId);
}

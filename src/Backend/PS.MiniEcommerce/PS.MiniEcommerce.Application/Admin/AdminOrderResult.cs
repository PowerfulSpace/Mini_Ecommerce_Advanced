using PS.MiniEcommerce.Application.Orders.DTOs;

namespace PS.MiniEcommerce.Application.Admin;

public sealed record AdminOrderResult(
    OrderDto Order,
    byte[] RowVersion,
    IReadOnlyList<object> DomainEvents);

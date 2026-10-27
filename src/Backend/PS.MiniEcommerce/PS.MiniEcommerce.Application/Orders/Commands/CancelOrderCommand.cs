using MediatR;
using PS.MiniEcommerce.Application.Orders.DTOs;

namespace PS.MiniEcommerce.Application.Orders.Commands;

public sealed record CancelOrderItemDto(Guid OrderItemId, int Quantity);

public sealed record CancelOrderCommand(
    Guid OrderId,
    string IfMatch,
    IReadOnlyList<CancelOrderItemDto>? Items = null) : IRequest<CancelOrderResponse>;

public sealed record CancelOrderResponse(
    OrderDto Order,
    byte[] RowVersion,
    IReadOnlyList<object> DomainEvents);

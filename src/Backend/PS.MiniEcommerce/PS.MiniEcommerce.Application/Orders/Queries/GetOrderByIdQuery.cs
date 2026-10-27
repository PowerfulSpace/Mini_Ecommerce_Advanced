using MediatR;
using PS.MiniEcommerce.Application.Orders.DTOs;

namespace PS.MiniEcommerce.Application.Orders.Queries;

public sealed record GetOrderByIdQuery(Guid OrderId) : IRequest<OrderDetailsResult>;

public sealed record OrderDetailsResult(OrderDto Order, byte[] RowVersion);

using MediatR;
using PS.MiniEcommerce.Application.Orders.DTOs;

namespace PS.MiniEcommerce.Application.Orders.Commands;

public sealed record CancelOrderCommand(Guid OrderId) : IRequest<OrderDto>;

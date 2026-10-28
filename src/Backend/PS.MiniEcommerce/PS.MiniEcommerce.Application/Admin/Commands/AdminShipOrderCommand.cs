using MediatR;

namespace PS.MiniEcommerce.Application.Admin.Commands;

public sealed record AdminShipOrderCommand(Guid OrderId)
    : IRequest<AdminOrderResult>;

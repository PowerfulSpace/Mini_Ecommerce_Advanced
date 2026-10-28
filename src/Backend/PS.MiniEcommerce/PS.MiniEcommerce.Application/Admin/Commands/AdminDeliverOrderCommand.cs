using MediatR;

namespace PS.MiniEcommerce.Application.Admin.Commands;

public sealed record AdminDeliverOrderCommand(Guid OrderId)
    : IRequest<AdminOrderResult>;

using MediatR;

namespace PS.MiniEcommerce.Application.Admin.Commands;

public sealed record AdminReturnOrderCommand(Guid OrderId)
    : IRequest<AdminOrderResult>;

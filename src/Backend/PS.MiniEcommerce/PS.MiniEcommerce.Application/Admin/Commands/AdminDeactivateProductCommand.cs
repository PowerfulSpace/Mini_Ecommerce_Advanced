using MediatR;

namespace PS.MiniEcommerce.Application.Admin.Commands;

public sealed record AdminDeactivateProductCommand(Guid ProductId)
    : IRequest<AdminProductResult>;

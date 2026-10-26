using MediatR;
using PS.MiniEcommerce.Application.Carts.Queries;

namespace PS.MiniEcommerce.Application.Carts.Commands;

public sealed record RemoveCartItemCommand(Guid ProductId, string? IfMatch)
    : IRequest<GetCartResult>;

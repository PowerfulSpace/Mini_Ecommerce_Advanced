using MediatR;
using PS.MiniEcommerce.Application.Carts.Queries;

namespace PS.MiniEcommerce.Application.Carts.Commands;

public sealed record AddCartItemCommand(Guid ProductId, int Quantity, string? IfMatch)
    : IRequest<GetCartResult>;

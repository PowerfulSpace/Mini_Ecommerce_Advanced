using MediatR;
using PS.MiniEcommerce.Application.Carts.Queries;

namespace PS.MiniEcommerce.Application.Carts.Commands;

public sealed record UpdateCartSelectionCommand(
    IReadOnlyList<CartItemSelectionDto> Items,
    string? IfMatch) : IRequest<GetCartResult>;

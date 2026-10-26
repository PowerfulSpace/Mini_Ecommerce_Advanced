using MediatR;

namespace PS.MiniEcommerce.Application.Carts.Queries;

public sealed record GetCartQuery : IRequest<GetCartResult>;

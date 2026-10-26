using MediatR;
using PS.MiniEcommerce.Application.Products.DTOs;

namespace PS.MiniEcommerce.Application.Products.Queries;

public sealed record GetProductByIdQuery(Guid ProductId)
    : IRequest<ProductDto>;

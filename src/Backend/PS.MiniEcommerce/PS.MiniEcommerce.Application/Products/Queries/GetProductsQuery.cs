using MediatR;
using PS.MiniEcommerce.Application.Common.Pagination;
using PS.MiniEcommerce.Application.Products.DTOs;

namespace PS.MiniEcommerce.Application.Products.Queries;

public sealed record GetProductsQuery(int Page, int PageSize)
    : IRequest<PaginatedResult<ProductDto>>;

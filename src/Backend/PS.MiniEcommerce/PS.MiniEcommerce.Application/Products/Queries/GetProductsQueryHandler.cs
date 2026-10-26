using MediatR;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Common.Pagination;
using PS.MiniEcommerce.Application.Products.DTOs;

namespace PS.MiniEcommerce.Application.Products.Queries;

public class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, PaginatedResult<ProductDto>>
{
    private readonly IProductRepository _productRepository;

    public GetProductsQueryHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<PaginatedResult<ProductDto>> Handle(
        GetProductsQuery request,
        CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _productRepository.GetActivePagedAsync(
            request.Page,
            request.PageSize,
            cancellationToken);

        var dtos = items
            .Select(product => new ProductDto(
                product.Id,
                product.Name,
                product.Description,
                product.Price,
                product.StockQuantity))
            .ToList();

        return new PaginatedResult<ProductDto>(dtos, request.Page, request.PageSize, totalCount);
    }
}

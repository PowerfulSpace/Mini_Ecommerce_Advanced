using MediatR;
using Microsoft.AspNetCore.Mvc;
using PS.MiniEcommerce.Application.Common.Pagination;
using PS.MiniEcommerce.Application.Products.Queries;

namespace PS.MiniEcommerce.API.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ProductsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> GetProducts(
        [FromQuery] int page = PaginationDefaults.DefaultPage,
        [FromQuery] int pageSize = PaginationDefaults.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetProductsQuery(page, pageSize),
            cancellationToken);

        return Ok(new
        {
            items = result.Items,
            page = result.Page,
            pageSize = result.PageSize,
            totalCount = result.TotalCount
        });
    }

    [HttpGet("{productId:guid}")]
    public async Task<IActionResult> GetProductById(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var product = await _mediator.Send(
            new GetProductByIdQuery(productId),
            cancellationToken);

        return Ok(product);
    }
}

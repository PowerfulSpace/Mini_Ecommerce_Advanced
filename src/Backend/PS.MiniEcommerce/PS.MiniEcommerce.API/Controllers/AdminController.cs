using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PS.MiniEcommerce.API.Contracts;
using PS.MiniEcommerce.API.Filters;
using PS.MiniEcommerce.Application.Admin.Commands;

namespace PS.MiniEcommerce.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "admin")]
[ServiceFilter(typeof(DevelopmentOnlyFilter), Order = int.MinValue)]
public class AdminController : ControllerBase
{
    private readonly IMediator _mediator;

    public AdminController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("products/{productId:guid}/deactivate")]
    public async Task<IActionResult> DeactivateProduct(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new AdminDeactivateProductCommand(productId),
            cancellationToken);

        return Ok(result.Product);
    }

    [HttpPost("products/{productId:guid}/stock")]
    public async Task<IActionResult> UpdateProductStock(
        Guid productId,
        [FromBody] UpdateProductStockRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new AdminUpdateProductStockCommand(productId, request.Available),
            cancellationToken);

        return Ok(result.Product);
    }
}

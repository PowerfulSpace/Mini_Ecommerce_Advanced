using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PS.MiniEcommerce.API.Contracts;
using PS.MiniEcommerce.API.Filters;
using PS.MiniEcommerce.Application.Admin;
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

    [HttpPost("orders/{orderId:guid}/ship")]
    public async Task<IActionResult> ShipOrder(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new AdminShipOrderCommand(orderId),
            cancellationToken);

        return OrderResult(result);
    }

    [HttpPost("orders/{orderId:guid}/deliver")]
    public async Task<IActionResult> DeliverOrder(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new AdminDeliverOrderCommand(orderId),
            cancellationToken);

        return OrderResult(result);
    }

    [HttpPost("orders/{orderId:guid}/return")]
    public async Task<IActionResult> ReturnOrder(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new AdminReturnOrderCommand(orderId),
            cancellationToken);

        return OrderResult(result);
    }

    private IActionResult OrderResult(AdminOrderResult result)
    {
        Response.Headers.ETag = $"\"{Convert.ToBase64String(result.RowVersion)}\"";
        return Ok(result.Order);
    }
}

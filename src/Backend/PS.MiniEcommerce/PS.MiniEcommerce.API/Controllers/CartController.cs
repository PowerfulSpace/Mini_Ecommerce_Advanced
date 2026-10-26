using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PS.MiniEcommerce.API.Contracts;
using PS.MiniEcommerce.Application.Carts.Commands;
using PS.MiniEcommerce.Application.Carts.Queries;

namespace PS.MiniEcommerce.API.Controllers;

[ApiController]
[Route("api/cart")]
[Authorize]
public class CartController : ControllerBase
{
    private readonly IMediator _mediator;

    public CartController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<IActionResult> GetCart(CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetCartQuery(), cancellationToken);

        Response.Headers.ETag = $"\"{result.Version}\"";

        return Ok(result.Cart);
    }

    [HttpPost("items")]
    public async Task<IActionResult> AddItem(
        [FromBody] AddCartItemRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new AddCartItemCommand(request.ProductId, request.Quantity, ifMatch),
            cancellationToken);

        Response.Headers.ETag = $"\"{result.Version}\"";

        return Ok(result.Cart);
    }

    [HttpPut("items/{productId:guid}")]
    public async Task<IActionResult> UpdateItem(
        Guid productId,
        [FromBody] UpdateCartItemRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new UpdateCartItemQuantityCommand(productId, request.Quantity, ifMatch),
            cancellationToken);

        Response.Headers.ETag = $"\"{result.Version}\"";

        return Ok(result.Cart);
    }

    [HttpDelete("items/{productId:guid}")]
    public async Task<IActionResult> RemoveItem(
        Guid productId,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new RemoveCartItemCommand(productId, ifMatch),
            cancellationToken);

        Response.Headers.ETag = $"\"{result.Version}\"";

        return Ok(result.Cart);
    }
}

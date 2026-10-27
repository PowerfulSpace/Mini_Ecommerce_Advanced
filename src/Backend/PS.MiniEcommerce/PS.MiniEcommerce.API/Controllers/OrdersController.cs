using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PS.MiniEcommerce.API.Contracts;
using PS.MiniEcommerce.Application.Checkout;
using PS.MiniEcommerce.Application.Checkout.Commands;
using PS.MiniEcommerce.Application.Common.Pagination;
using PS.MiniEcommerce.Application.Orders.Commands;
using PS.MiniEcommerce.Application.Orders.Queries;

namespace PS.MiniEcommerce.API.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrdersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        CancellationToken cancellationToken = default)
    {
        var fingerprint = ComputeFingerprint(ifMatch);

        var result = await _mediator.Send(
            new CheckoutCommand(idempotencyKey ?? string.Empty, ifMatch, fingerprint),
            cancellationToken);

        if (result.NewOrderRowVersion is not null)
        {
            Response.Headers.ETag = $"\"{Convert.ToBase64String(result.NewOrderRowVersion)}\"";
        }

        if (result.Success && result.OrderId is not null)
        {
            Response.Headers.Location = $"/api/orders/{result.OrderId}";
        }

        var isProblemDetails = result.ResponseStatusCode >= StatusCodes.Status400BadRequest;

        return new ContentResult
        {
            Content = isProblemDetails
                ? EnsureProblemDetailsInstance(result.ResponsePayload, Request.Path)
                : result.ResponsePayload,
            ContentType = isProblemDetails ? "application/problem+json" : "application/json",
            StatusCode = result.ResponseStatusCode
        };
    }

    [HttpGet]
    public async Task<IActionResult> GetOrders(
        [FromQuery] int page = PaginationDefaults.DefaultPage,
        [FromQuery] int pageSize = PaginationDefaults.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetOrdersQuery(page, pageSize),
            cancellationToken);

        return Ok(new
        {
            items = result.Items,
            page = result.Page,
            pageSize = result.PageSize,
            totalCount = result.TotalCount
        });
    }

    [HttpGet("{orderId:guid}")]
    public async Task<IActionResult> GetOrderById(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetOrderByIdQuery(orderId),
            cancellationToken);

        Response.Headers.ETag = $"\"{Convert.ToBase64String(result.RowVersion)}\"";

        return Ok(result.Order);
    }

    [HttpPost("{orderId:guid}/cancel")]
    public async Task<IActionResult> Cancel(
        Guid orderId,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        [FromBody] CancelOrderRequest? request,
        CancellationToken cancellationToken = default)
    {
        var items = request?.Items?.Select(item => new CancelOrderItemDto(item.OrderItemId, item.Quantity)).ToList();

        var result = await _mediator.Send(
            new CancelOrderCommand(orderId, ifMatch ?? string.Empty, items),
            cancellationToken);

        Response.Headers.ETag = $"\"{Convert.ToBase64String(result.RowVersion)}\"";

        return Ok(result.Order);
    }

    private static string EnsureProblemDetailsInstance(string payload, PathString path)
    {
        try
        {
            if (JsonNode.Parse(payload) is not JsonObject problem)
            {
                return payload;
            }

            if (!problem.ContainsKey("instance"))
            {
                problem["instance"] = path.Value ?? "/api/orders";
            }

            return problem.ToJsonString();
        }
        catch (JsonException)
        {
            return payload;
        }
    }

    private static byte[] ComputeFingerprint(string? ifMatch)
    {
        var input = $"POST /api/orders\n{ifMatch ?? string.Empty}";
        var bytes = Encoding.UTF8.GetBytes(input);
        return SHA256.HashData(bytes);
    }
}

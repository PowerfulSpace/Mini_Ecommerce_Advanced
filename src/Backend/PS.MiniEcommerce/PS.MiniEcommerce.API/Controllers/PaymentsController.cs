using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PS.MiniEcommerce.API.Contracts;
using PS.MiniEcommerce.API.Filters;
using PS.MiniEcommerce.Application.Payments.Commands;

namespace PS.MiniEcommerce.API.Controllers;

[ApiController]
[Route("api/payments")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly IMediator _mediator;

    public PaymentsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("{paymentId:guid}/confirm")]
    public async Task<IActionResult> Confirm(
        Guid paymentId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var fingerprint = ComputeFingerprint(paymentId);

        var result = await _mediator.Send(
            new ConfirmPaymentCommand(paymentId, idempotencyKey ?? string.Empty, fingerprint),
            cancellationToken);

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

    [HttpPost("{paymentId:guid}/refund")]
    [Authorize(Roles = "admin")]
    [ServiceFilter(typeof(DevelopmentOnlyFilter), Order = int.MinValue)]
    public async Task<IActionResult> Refund(
        Guid paymentId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] RefundPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        var fingerprint = ComputeRefundFingerprint(paymentId, request.Amount, request.Reason);

        var result = await _mediator.Send(
            new RefundPaymentCommand(
                paymentId,
                request.Amount,
                request.Reason,
                idempotencyKey ?? string.Empty,
                fingerprint),
            cancellationToken);

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
                problem["instance"] = path.Value ?? "/api/payments";
            }

            return problem.ToJsonString();
        }
        catch (JsonException)
        {
            return payload;
        }
    }

    private static byte[] ComputeFingerprint(Guid paymentId)
    {
        var id = paymentId.ToString();
        var input = $"POST /api/payments/{id}/confirm\n{id}";
        var bytes = Encoding.UTF8.GetBytes(input);
        return SHA256.HashData(bytes);
    }

    private static byte[] ComputeRefundFingerprint(Guid paymentId, decimal amount, string? reason)
    {
        var id = paymentId.ToString();
        var normalizedAmount = decimal
            .Round(amount, 2, MidpointRounding.AwayFromZero)
            .ToString("0.00", CultureInfo.InvariantCulture);
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? string.Empty : reason.Trim();
        var input = $"POST /api/payments/{id}/refund\n{id}\n{normalizedAmount}\n{normalizedReason}";
        return SHA256.HashData(Encoding.UTF8.GetBytes(input));
    }
}

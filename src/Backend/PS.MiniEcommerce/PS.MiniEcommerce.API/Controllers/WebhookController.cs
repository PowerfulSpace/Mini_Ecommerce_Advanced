using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PS.MiniEcommerce.Application.Webhooks;

namespace PS.MiniEcommerce.API.Controllers;

[ApiController]
[Route("api/webhooks")]
[AllowAnonymous]
public class WebhookController : ControllerBase
{
    private readonly IWebhookHandler _webhookHandler;

    public WebhookController(IWebhookHandler webhookHandler)
    {
        _webhookHandler = webhookHandler;
    }

    [HttpPost("stripe")]
    public async Task<IActionResult> Stripe(
        [FromHeader(Name = "Stripe-Signature")] string? stripeSignature,
        CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var payload = await reader.ReadToEndAsync(cancellationToken);

        var result = await _webhookHandler.HandleAsync(
            payload,
            stripeSignature ?? string.Empty,
            cancellationToken);

        if (result.Body is null)
        {
            return StatusCode(result.StatusCode);
        }

        return new ContentResult
        {
            Content = result.Body,
            ContentType = "application/json",
            StatusCode = result.StatusCode
        };
    }
}

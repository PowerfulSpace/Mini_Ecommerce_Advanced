namespace PS.MiniEcommerce.Application.Webhooks;

public interface IWebhookHandler
{
    Task<WebhookResult> HandleAsync(
        string payload,
        string signatureHeader,
        CancellationToken cancellationToken);
}

using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Application.Webhooks;

public interface IWebhookTransactionExecutor
{
    Task<WebhookExecutionResult> ExecutePaymentIntentSucceededAsync(
        string eventId,
        string eventType,
        Guid paymentId,
        Money amount,
        DateTime utcNow,
        CancellationToken cancellationToken);
}

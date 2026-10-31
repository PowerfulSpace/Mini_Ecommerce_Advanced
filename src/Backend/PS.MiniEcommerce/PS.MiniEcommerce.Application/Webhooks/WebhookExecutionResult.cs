namespace PS.MiniEcommerce.Application.Webhooks;

public sealed record WebhookExecutionResult(
    bool Processed,
    IReadOnlyList<object> DomainEvents);

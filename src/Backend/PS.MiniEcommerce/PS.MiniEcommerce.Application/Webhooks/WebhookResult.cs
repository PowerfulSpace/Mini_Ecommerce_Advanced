namespace PS.MiniEcommerce.Application.Webhooks;

public sealed record WebhookResult(int StatusCode, string? Body = null);

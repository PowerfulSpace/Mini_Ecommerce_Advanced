namespace PS.MiniEcommerce.Application.Checkout;

public sealed record CheckoutExecutionResult(
    bool Success,
    int ResponseStatusCode,
    string ResponsePayload,
    Guid? OrderId,
    byte[]? NewCartRowVersion);

namespace PS.MiniEcommerce.Application.Payments;

public sealed record ConfirmExecutionResult(
    bool Success,
    int ResponseStatusCode,
    string ResponsePayload,
    Guid OrderId,
    Guid PaymentId,
    IReadOnlyList<object> DomainEvents);

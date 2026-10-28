namespace PS.MiniEcommerce.Application.Payments;

public sealed record RefundExecutionResult(
    bool Success,
    int ResponseStatusCode,
    string ResponsePayload,
    Guid OrderId,
    Guid PaymentId,
    IReadOnlyList<object> DomainEvents);

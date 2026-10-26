namespace PS.MiniEcommerce.Application.Abstractions.Persistence;

public sealed record CheckoutAttemptData(
    Guid Id,
    Guid UserId,
    string IdempotencyKey,
    byte[] RequestFingerprint,
    CheckoutAttemptState State,
    Guid? OrderId,
    int? ResponseStatusCode,
    string? ResponsePayload,
    DateTime LeaseExpiresAtUtc);

public enum CheckoutAttemptState
{
    InProgress,
    Completed
}

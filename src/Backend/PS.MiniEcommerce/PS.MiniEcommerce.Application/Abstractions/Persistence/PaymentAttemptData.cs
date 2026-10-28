namespace PS.MiniEcommerce.Application.Abstractions.Persistence;

public sealed record PaymentAttemptData(
    Guid Id,
    Guid UserId,
    string IdempotencyKey,
    byte[] RequestFingerprint,
    PaymentAttemptState State,
    Guid? PaymentId,
    int? ResponseStatusCode,
    string? ResponsePayload,
    DateTime LeaseExpiresAtUtc);

public enum PaymentAttemptState
{
    InProgress,
    Completed
}

namespace PS.MiniEcommerce.Application.Abstractions.Persistence;

public interface IPaymentAttemptRepository
{
    Task<PaymentAttemptData?> GetByUserAndKeyAsync(
        Guid userId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<PaymentAttemptData?> ReserveAsync(
        Guid userId,
        string idempotencyKey,
        byte[] requestFingerprint,
        DateTime utcNow,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<PaymentAttemptData?> WaitForCompletionAsync(
        Guid userId,
        string idempotencyKey,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    Task<bool> TryClaimExpiredAsync(
        Guid attemptId,
        DateTime utcNow,
        TimeSpan newLeaseDuration,
        CancellationToken cancellationToken);

    Task<bool> TryReopenFailedAsync(
        Guid attemptId,
        DateTime utcNow,
        TimeSpan newLeaseDuration,
        CancellationToken cancellationToken);

    Task CompleteWithErrorAsync(
        Guid attemptId,
        int statusCode,
        string responsePayload,
        DateTime utcNow,
        CancellationToken cancellationToken);
}

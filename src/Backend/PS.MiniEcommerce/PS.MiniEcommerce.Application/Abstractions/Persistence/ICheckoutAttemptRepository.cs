namespace PS.MiniEcommerce.Application.Abstractions.Persistence;

public interface ICheckoutAttemptRepository
{
    Task<CheckoutAttemptData?> GetByUserAndKeyAsync(
        Guid userId,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<CheckoutAttemptData?> ReserveAsync(
        Guid userId,
        string idempotencyKey,
        byte[] requestFingerprint,
        DateTime utcNow,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task<CheckoutAttemptData?> WaitForCompletionAsync(
        Guid userId,
        string idempotencyKey,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    Task<bool> TryClaimExpiredAsync(
        Guid attemptId,
        DateTime utcNow,
        TimeSpan newLeaseDuration,
        CancellationToken cancellationToken);
}

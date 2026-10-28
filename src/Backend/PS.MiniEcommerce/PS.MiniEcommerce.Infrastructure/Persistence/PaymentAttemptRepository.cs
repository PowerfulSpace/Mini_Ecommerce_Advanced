using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Application.Abstractions.Persistence;

namespace PS.MiniEcommerce.Infrastructure.Persistence;

public class PaymentAttemptRepository : IPaymentAttemptRepository
{
    private const string PaymentFailedCode = "payment_failed";

    private readonly AppDbContext _dbContext;

    public PaymentAttemptRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PaymentAttemptData?> GetByUserAndKeyAsync(
        Guid userId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var attempt = await _dbContext.PaymentAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.UserId == userId && a.IdempotencyKey == idempotencyKey,
                cancellationToken);

        return attempt is null ? null : ToData(attempt);
    }

    public async Task<PaymentAttemptData?> ReserveAsync(
        Guid userId,
        string idempotencyKey,
        byte[] requestFingerprint,
        DateTime utcNow,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var attempt = PaymentAttempt.Create(
            userId,
            idempotencyKey,
            requestFingerprint,
            utcNow,
            leaseDuration);

        _dbContext.PaymentAttempts.Add(attempt);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return ToData(attempt);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            _dbContext.Entry(attempt).State = EntityState.Detached;
            return null;
        }
    }

    public async Task<PaymentAttemptData?> WaitForCompletionAsync(
        Guid userId,
        string idempotencyKey,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        var pollInterval = TimeSpan.FromMilliseconds(100);

        while (DateTime.UtcNow < deadline)
        {
            var attempt = await GetByUserAndKeyAsync(userId, idempotencyKey, cancellationToken);

            if (attempt is null)
            {
                return null;
            }

            if (attempt.State == PaymentAttemptState.Completed)
            {
                return attempt;
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var delay = remaining < pollInterval ? remaining : pollInterval;
            await Task.Delay(delay, cancellationToken);
        }

        return null;
    }

    public async Task<bool> TryClaimExpiredAsync(
        Guid attemptId,
        DateTime utcNow,
        TimeSpan newLeaseDuration,
        CancellationToken cancellationToken)
    {
        var newLeaseExpiresAt = utcNow + newLeaseDuration;

        var affected = await _dbContext.PaymentAttempts
            .Where(attempt =>
                attempt.Id == attemptId
                && attempt.Status == PaymentAttemptStatus.InProgress
                && attempt.LeaseExpiresAtUtc < utcNow)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(attempt => attempt.LeaseExpiresAtUtc, newLeaseExpiresAt)
                    .SetProperty(attempt => attempt.UpdatedAtUtc, utcNow),
                cancellationToken);

        return affected == 1;
    }

    public async Task<bool> TryReopenFailedAsync(
        Guid attemptId,
        DateTime utcNow,
        TimeSpan newLeaseDuration,
        CancellationToken cancellationToken)
    {
        var newLeaseExpiresAt = utcNow + newLeaseDuration;

        var affected = await _dbContext.PaymentAttempts
            .Where(attempt =>
                attempt.Id == attemptId
                && attempt.Status == PaymentAttemptStatus.Completed
                && attempt.ResponseStatusCode == 409
                && attempt.ResponsePayload != null
                && attempt.ResponsePayload.Contains(PaymentFailedCode))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(attempt => attempt.Status, PaymentAttemptStatus.InProgress)
                    .SetProperty(attempt => attempt.LeaseExpiresAtUtc, newLeaseExpiresAt)
                    .SetProperty(attempt => attempt.ResponseStatusCode, (int?)null)
                    .SetProperty(attempt => attempt.ResponsePayload, (string?)null)
                    .SetProperty(attempt => attempt.UpdatedAtUtc, utcNow),
                cancellationToken);

        return affected == 1;
    }

    public async Task CompleteAsync(
        Guid attemptId,
        Guid paymentId,
        int statusCode,
        string responsePayload,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var attempt = await _dbContext.PaymentAttempts
            .FirstAsync(paymentAttempt => paymentAttempt.Id == attemptId, cancellationToken);

        attempt.MarkCompleted(paymentId, statusCode, responsePayload, utcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteWithErrorAsync(
        Guid attemptId,
        int statusCode,
        string responsePayload,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var attempt = await _dbContext.PaymentAttempts
            .FirstAsync(paymentAttempt => paymentAttempt.Id == attemptId, cancellationToken);

        attempt.MarkCompletedWithError(statusCode, responsePayload, utcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static PaymentAttemptData ToData(PaymentAttempt attempt) => new(
        attempt.Id,
        attempt.UserId,
        attempt.IdempotencyKey,
        attempt.RequestFingerprint,
        attempt.Status == PaymentAttemptStatus.InProgress
            ? PaymentAttemptState.InProgress
            : PaymentAttemptState.Completed,
        attempt.PaymentId,
        attempt.ResponseStatusCode,
        attempt.ResponsePayload,
        attempt.LeaseExpiresAtUtc);

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        return exception.InnerException is Microsoft.Data.SqlClient.SqlException sqlException
            && (sqlException.Number == 2601 || sqlException.Number == 2627);
    }
}

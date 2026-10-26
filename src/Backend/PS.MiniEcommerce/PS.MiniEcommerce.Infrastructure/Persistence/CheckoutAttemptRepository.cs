using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Application.Abstractions.Persistence;

namespace PS.MiniEcommerce.Infrastructure.Persistence;

public class CheckoutAttemptRepository : ICheckoutAttemptRepository
{
    private readonly AppDbContext _dbContext;

    public CheckoutAttemptRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CheckoutAttemptData?> GetByUserAndKeyAsync(
        Guid userId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var attempt = await _dbContext.CheckoutAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.UserId == userId && a.IdempotencyKey == idempotencyKey,
                cancellationToken);

        return attempt is null ? null : ToData(attempt);
    }

    public async Task<CheckoutAttemptData?> ReserveAsync(
        Guid userId,
        string idempotencyKey,
        byte[] requestFingerprint,
        DateTime utcNow,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var attempt = CheckoutAttempt.Create(
            userId,
            idempotencyKey,
            requestFingerprint,
            utcNow,
            leaseDuration);

        _dbContext.CheckoutAttempts.Add(attempt);

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

    public async Task<CheckoutAttemptData?> WaitForCompletionAsync(
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

            if (attempt.State == CheckoutAttemptState.Completed)
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

        var affected = await _dbContext.CheckoutAttempts
            .Where(attempt =>
                attempt.Id == attemptId
                && attempt.Status == CheckoutAttemptStatus.InProgress
                && attempt.LeaseExpiresAtUtc < utcNow)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(attempt => attempt.LeaseExpiresAtUtc, newLeaseExpiresAt)
                    .SetProperty(attempt => attempt.UpdatedAtUtc, utcNow),
                cancellationToken);

        return affected == 1;
    }

    private static CheckoutAttemptData ToData(CheckoutAttempt attempt) => new(
        attempt.Id,
        attempt.UserId,
        attempt.IdempotencyKey,
        attempt.RequestFingerprint,
        attempt.Status == CheckoutAttemptStatus.InProgress
            ? CheckoutAttemptState.InProgress
            : CheckoutAttemptState.Completed,
        attempt.OrderId,
        attempt.ResponseStatusCode,
        attempt.ResponsePayload,
        attempt.LeaseExpiresAtUtc);

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        return exception.InnerException is Microsoft.Data.SqlClient.SqlException sqlException
            && (sqlException.Number == 2601 || sqlException.Number == 2627);
    }
}

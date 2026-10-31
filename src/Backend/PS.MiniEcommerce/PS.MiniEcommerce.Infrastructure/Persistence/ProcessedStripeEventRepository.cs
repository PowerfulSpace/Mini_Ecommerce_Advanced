using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Application.Abstractions.Persistence;

namespace PS.MiniEcommerce.Infrastructure.Persistence;

public class ProcessedStripeEventRepository : IProcessedStripeEventRepository
{
    private readonly AppDbContext _dbContext;

    public ProcessedStripeEventRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken)
    {
        return _dbContext.ProcessedStripeEvents
            .AsNoTracking()
            .AnyAsync(
                processedEvent => processedEvent.EventId == eventId,
                cancellationToken);
    }

    public async Task<bool> TryAddAsync(
        string eventId,
        string type,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var processedEvent = ProcessedStripeEvent.Create(eventId, type, utcNow);
        _dbContext.ProcessedStripeEvents.Add(processedEvent);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            _dbContext.Entry(processedEvent).State = EntityState.Detached;
            return false;
        }
    }

    internal static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException { Number: 2601 or 2627 };
    }
}

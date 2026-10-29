namespace PS.MiniEcommerce.Application.Abstractions.Outbox;

public interface IOutboxRepository
{
    Task AddAsync(object domainEvent, DateTime utcNow, CancellationToken cancellationToken);

    Task<IReadOnlyList<OutboxMessageData>> GetPendingAsync(
        int batchSize,
        DateTime utcNow,
        CancellationToken cancellationToken);

    Task MarkProcessedAsync(Guid id, DateTime utcNow, CancellationToken cancellationToken);

    Task MarkFailedAttemptAsync(
        Guid id,
        string error,
        DateTime utcNow,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

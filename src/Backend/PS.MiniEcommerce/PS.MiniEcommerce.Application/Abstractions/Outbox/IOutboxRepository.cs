namespace PS.MiniEcommerce.Application.Abstractions.Outbox;

public interface IOutboxRepository
{
    Task AddAsync(object domainEvent, DateTime utcNow, CancellationToken cancellationToken);
}

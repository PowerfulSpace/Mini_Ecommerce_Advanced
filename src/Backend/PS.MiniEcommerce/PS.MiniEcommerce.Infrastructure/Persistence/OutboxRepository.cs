using System.Text.Json;
using PS.MiniEcommerce.Application.Abstractions.Outbox;
using PS.MiniEcommerce.Infrastructure.Serialization;

namespace PS.MiniEcommerce.Infrastructure.Persistence;

public class OutboxRepository : IOutboxRepository
{
    private readonly AppDbContext _dbContext;

    public OutboxRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(object domainEvent, DateTime utcNow, CancellationToken cancellationToken)
    {
        var eventType = domainEvent.GetType().FullName
            ?? throw new InvalidOperationException("Domain event type does not have a full name.");

        var payload = JsonSerializer.Serialize(
            domainEvent,
            domainEvent.GetType(),
            OutboxJsonOptions.Instance);

        _dbContext.OutboxMessages.Add(OutboxMessage.Create(eventType, payload, utcNow));

        return Task.CompletedTask;
    }
}

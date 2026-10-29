using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Application.Abstractions.Outbox;
using PS.MiniEcommerce.Infrastructure.Serialization;

namespace PS.MiniEcommerce.Infrastructure.Persistence;

public class OutboxRepository : IOutboxRepository
{
    private const int MaxAttempts = 5;

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

    public async Task<IReadOnlyList<OutboxMessageData>> GetPendingAsync(
        int batchSize,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var oneSecondAgo = utcNow.AddSeconds(-1);
        var twoSecondsAgo = utcNow.AddSeconds(-2);
        var fourSecondsAgo = utcNow.AddSeconds(-4);
        var eightSecondsAgo = utcNow.AddSeconds(-8);
        var sixteenSecondsAgo = utcNow.AddSeconds(-16);

        var messages = await _dbContext.OutboxMessages
            .AsNoTracking()
            .Where(message => message.ProcessedAtUtc == null && message.Attempts < MaxAttempts)
            .Where(message =>
                message.LastAttemptAtUtc == null
                || (message.Attempts == 0 && message.LastAttemptAtUtc <= oneSecondAgo)
                || (message.Attempts == 1 && message.LastAttemptAtUtc <= twoSecondsAgo)
                || (message.Attempts == 2 && message.LastAttemptAtUtc <= fourSecondsAgo)
                || (message.Attempts == 3 && message.LastAttemptAtUtc <= eightSecondsAgo)
                || (message.Attempts == 4 && message.LastAttemptAtUtc <= sixteenSecondsAgo))
            .OrderBy(message => message.CreatedAtUtc)
            .Take(batchSize)
            .Select(message => new OutboxMessageData(
                message.Id,
                message.EventType,
                message.Payload,
                message.CreatedAtUtc,
                message.Attempts))
            .ToListAsync(cancellationToken);

        return messages;
    }

    public async Task MarkProcessedAsync(
        Guid id,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var message = await _dbContext.OutboxMessages
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (message is null)
        {
            return;
        }

        message.MarkProcessed(utcNow);
    }

    public async Task MarkFailedAttemptAsync(
        Guid id,
        string error,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var message = await _dbContext.OutboxMessages
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (message is null)
        {
            return;
        }

        message.MarkFailedAttempt(error, utcNow, MaxAttempts);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}

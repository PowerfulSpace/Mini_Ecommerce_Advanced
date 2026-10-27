using PS.MiniEcommerce.Application.Abstractions.Persistence;

namespace PS.MiniEcommerce.Infrastructure.Persistence;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly AppDbContext _dbContext;

    public AuditLogRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken)
    {
        var auditLog = new AuditLog(
            Guid.NewGuid(),
            entry.OccurredAtUtc,
            entry.ActorUserId,
            entry.Action,
            entry.EntityType,
            entry.EntityId,
            entry.Payload,
            entry.CorrelationId);

        _dbContext.AuditLogs.Add(auditLog);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

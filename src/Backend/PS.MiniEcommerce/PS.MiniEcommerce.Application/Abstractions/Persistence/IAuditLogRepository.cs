namespace PS.MiniEcommerce.Application.Abstractions.Persistence;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken);
}

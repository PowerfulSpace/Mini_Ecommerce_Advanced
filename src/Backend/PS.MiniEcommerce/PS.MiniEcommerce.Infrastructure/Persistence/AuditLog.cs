namespace PS.MiniEcommerce.Infrastructure.Persistence;

public class AuditLog
{
    public Guid Id { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public Guid? ActorUserId { get; private set; }

    public string Action { get; private set; }

    public string EntityType { get; private set; }

    public Guid EntityId { get; private set; }

    public string? Payload { get; private set; }

    public string? CorrelationId { get; private set; }

    private AuditLog()
    {
        Action = string.Empty;
        EntityType = string.Empty;
    }

    public AuditLog(
        Guid id,
        DateTime occurredAtUtc,
        Guid? actorUserId,
        string action,
        string entityType,
        Guid entityId,
        string? payload,
        string? correlationId)
    {
        Id = id;
        OccurredAtUtc = occurredAtUtc;
        ActorUserId = actorUserId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        Payload = payload;
        CorrelationId = correlationId;
    }
}

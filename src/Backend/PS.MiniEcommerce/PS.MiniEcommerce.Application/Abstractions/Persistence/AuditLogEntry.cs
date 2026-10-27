namespace PS.MiniEcommerce.Application.Abstractions.Persistence;

public sealed record AuditLogEntry(
    Guid? ActorUserId,
    string Action,
    string EntityType,
    Guid EntityId,
    string? Payload,
    DateTime OccurredAtUtc,
    string? CorrelationId);

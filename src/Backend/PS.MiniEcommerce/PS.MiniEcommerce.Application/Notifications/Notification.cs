namespace PS.MiniEcommerce.Application.Notifications;

public sealed record Notification(
    string EventType,
    Guid EntityId,
    Guid? OrderId,
    Guid UserId,
    DateTime CreatedAtUtc,
    DateTime OccurredAtUtc,
    string Message,
    decimal? Amount);

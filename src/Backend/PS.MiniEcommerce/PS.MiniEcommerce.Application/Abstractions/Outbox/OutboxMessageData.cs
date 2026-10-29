namespace PS.MiniEcommerce.Application.Abstractions.Outbox;

public sealed record OutboxMessageData(
    Guid Id,
    string EventType,
    string Payload,
    DateTime CreatedAtUtc,
    int Attempts);

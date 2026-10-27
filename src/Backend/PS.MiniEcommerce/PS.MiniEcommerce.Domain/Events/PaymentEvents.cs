namespace PS.MiniEcommerce.Domain.Events;

public sealed record PaymentConfirmed(
    Guid PaymentId,
    Guid OrderId,
    Money Amount,
    DateTime OccurredAtUtc);

public sealed record PaymentRefunded(
    Guid PaymentId,
    Guid OrderId,
    Money Amount,
    DateTime OccurredAtUtc);

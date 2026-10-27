namespace PS.MiniEcommerce.Domain.Events;

public sealed record StockAdjusted(
    Guid ProductId,
    int NewAvailable,
    DateTime OccurredAtUtc);

public sealed record ProductDeactivated(
    Guid ProductId,
    DateTime OccurredAtUtc);

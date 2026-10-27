namespace PS.MiniEcommerce.Domain.Events;

public sealed record OrderPlaced(
    Guid OrderId,
    Guid UserId,
    Money TotalAmount,
    int ItemCount,
    DateTime OccurredAtUtc);

public sealed record OrderPaid(
    Guid OrderId,
    Guid UserId,
    DateTime OccurredAtUtc);

public sealed record OrderShipped(
    Guid OrderId,
    Guid UserId,
    DateTime OccurredAtUtc);

public sealed record OrderDelivered(
    Guid OrderId,
    Guid UserId,
    DateTime OccurredAtUtc);

public sealed record OrderCancelled(
    Guid OrderId,
    Guid UserId,
    bool IsFullCancellation,
    IReadOnlyList<CancelledItem> Items,
    DateTime OccurredAtUtc);

public sealed record CancelledItem(
    Guid OrderItemId,
    Guid ProductId,
    int Quantity);

public sealed record OrderReturned(
    Guid OrderId,
    Guid UserId,
    DateTime OccurredAtUtc);

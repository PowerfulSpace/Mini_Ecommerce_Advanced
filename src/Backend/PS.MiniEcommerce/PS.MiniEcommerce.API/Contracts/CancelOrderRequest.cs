namespace PS.MiniEcommerce.API.Contracts;

public sealed record CancelOrderRequest(IReadOnlyList<CancelOrderItemRequest>? Items);

public sealed record CancelOrderItemRequest(Guid OrderItemId, int Quantity);

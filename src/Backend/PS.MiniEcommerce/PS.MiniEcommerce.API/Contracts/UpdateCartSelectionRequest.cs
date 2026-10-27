namespace PS.MiniEcommerce.API.Contracts;

public sealed record UpdateCartSelectionRequest(IReadOnlyList<CartItemSelectionRequest> Items);

namespace PS.MiniEcommerce.API.Contracts;

public sealed record CartItemSelectionRequest(Guid ProductId, bool IsSelected);

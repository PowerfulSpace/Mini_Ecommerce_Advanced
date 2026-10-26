namespace PS.MiniEcommerce.API.Contracts;

public sealed record AddCartItemRequest(Guid ProductId, int Quantity);

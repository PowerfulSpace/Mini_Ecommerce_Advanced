namespace PS.MiniEcommerce.Application.Carts.Commands;

public sealed record CartItemSelectionDto(Guid ProductId, bool IsSelected);

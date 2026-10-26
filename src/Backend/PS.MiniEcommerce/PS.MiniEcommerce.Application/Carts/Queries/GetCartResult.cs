using PS.MiniEcommerce.Application.Carts.DTOs;

namespace PS.MiniEcommerce.Application.Carts.Queries;

public sealed record GetCartResult(CartDto Cart, string Version);

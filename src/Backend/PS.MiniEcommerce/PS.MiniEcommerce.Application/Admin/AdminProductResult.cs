using PS.MiniEcommerce.Application.Products.DTOs;

namespace PS.MiniEcommerce.Application.Admin;

public sealed record AdminProductResult(
    ProductDto Product,
    IReadOnlyList<object> DomainEvents);

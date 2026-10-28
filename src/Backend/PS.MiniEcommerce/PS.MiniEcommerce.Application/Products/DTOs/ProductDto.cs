using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Application.Products.DTOs;

public sealed record ProductDto(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    int Available)
{
    public static ProductDto FromProduct(Product product) => new(
        product.Id,
        product.Name,
        product.Description,
        product.Price.Amount,
        product.Available);
}

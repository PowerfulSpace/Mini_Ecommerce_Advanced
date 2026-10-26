namespace PS.MiniEcommerce.Application.Products.DTOs;

public sealed record ProductDto(
    Guid Id,
    string Name,
    string Description,
    decimal Price,
    int StockQuantity);

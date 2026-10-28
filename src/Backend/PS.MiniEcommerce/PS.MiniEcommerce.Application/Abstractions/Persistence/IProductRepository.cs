using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Application.Abstractions.Persistence;

public interface IProductRepository
{
    Task<(IReadOnlyList<Product> Items, int TotalCount)> GetActivePagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<Product?> GetActiveByIdAsync(Guid productId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Product>> GetByIdsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken);

    Task<Product?> GetByIdAsync(Guid productId, CancellationToken cancellationToken);

    Task<(Product? Product, IReadOnlyList<object> DomainEvents)> DeactivateAsync(
        Guid productId,
        DateTime utcNow,
        CancellationToken cancellationToken);

    Task<(AdminStockUpdateResult Result, Product? Product, IReadOnlyList<object> DomainEvents)> AdjustAvailableAsync(
        Guid productId,
        int available,
        DateTime utcNow,
        CancellationToken cancellationToken);
}

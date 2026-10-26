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
}

using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Infrastructure.Persistence;

public class ProductRepository : IProductRepository
{
    private readonly AppDbContext _dbContext;

    public ProductRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<(IReadOnlyList<Product> Items, int TotalCount)> GetActivePagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Products
            .AsNoTracking()
            .Where(product => product.IsActive);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(product => product.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<Product?> GetActiveByIdAsync(Guid productId, CancellationToken cancellationToken)
    {
        return await _dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(
                product => product.Id == productId && product.IsActive,
                cancellationToken);
    }

    public async Task<IReadOnlyList<Product>> GetByIdsAsync(
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0)
        {
            return Array.Empty<Product>();
        }

        return await _dbContext.Products
            .AsNoTracking()
            .Where(product => productIds.Contains(product.Id))
            .ToListAsync(cancellationToken);
    }
}

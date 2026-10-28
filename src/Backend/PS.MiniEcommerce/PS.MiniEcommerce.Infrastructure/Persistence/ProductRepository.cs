using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Exceptions;

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

    public async Task<Product?> GetByIdAsync(Guid productId, CancellationToken cancellationToken)
    {
        return await _dbContext.Products
            .FirstOrDefaultAsync(product => product.Id == productId, cancellationToken);
    }

    public async Task<(Product? Product, IReadOnlyList<object> DomainEvents)> DeactivateAsync(
        Guid productId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var product = await GetByIdAsync(productId, cancellationToken);

        if (product is null)
        {
            return (null, Array.Empty<object>());
        }

        product.Deactivate(utcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var domainEvents = product.DomainEvents.ToList();
        product.ClearDomainEvents();

        return (product, domainEvents);
    }

    public async Task<(AdminStockUpdateResult Result, Product? Product, IReadOnlyList<object> DomainEvents)> AdjustAvailableAsync(
        Guid productId,
        int available,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var product = await _dbContext.Products
                .FromSqlInterpolated(
                    $"SELECT * FROM Products WITH (UPDLOCK) WHERE Id = {productId}")
                .FirstOrDefaultAsync(cancellationToken);

            if (product is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (AdminStockUpdateResult.NotFound, null, Array.Empty<object>());
            }

            var currentAvailable = product.Available;

            if (currentAvailable == available)
            {
                await transaction.RollbackAsync(cancellationToken);
                return (AdminStockUpdateResult.Success, product, Array.Empty<object>());
            }

            product.AdjustAvailable(available, utcNow);

            var affected = await _dbContext.Products
                .Where(product => product.Id == productId && product.Available == currentAvailable)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        product => product.Available,
                        available),
                    cancellationToken);

            if (affected == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();
                continue;
            }

            var domainEvents = product.DomainEvents.ToList();
            product.ClearDomainEvents();

            await transaction.CommitAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();

            return (AdminStockUpdateResult.Success, product, domainEvents);
        }

        throw new DomainConflictException(
            "The product stock was changed concurrently. Retry the operation.",
            "admin_stock_conflict");
    }
}

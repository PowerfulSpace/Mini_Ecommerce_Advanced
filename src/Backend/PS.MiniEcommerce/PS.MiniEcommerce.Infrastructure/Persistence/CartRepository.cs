using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Infrastructure.Persistence;

public class CartRepository : ICartRepository
{
    private readonly AppDbContext _dbContext;

    public CartRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Cart?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.Carts
            .Include(cart => cart.Items)
            .FirstOrDefaultAsync(cart => cart.UserId == userId, cancellationToken);
    }

    public async Task<Cart> CreateAsync(Guid userId, DateTime utcNow, CancellationToken cancellationToken)
    {
        var cart = Cart.Create(userId, utcNow);
        _dbContext.Carts.Add(cart);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return cart;
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            _dbContext.Entry(cart).State = EntityState.Detached;

            var existing = await GetByUserIdAsync(userId, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return existing;
        }
    }

    public async Task<byte[]> GetRowVersionAsync(Guid cartId, CancellationToken cancellationToken)
    {
        return await _dbContext.Carts
            .AsNoTracking()
            .Where(cart => cart.Id == cartId)
            .Select(cart => EF.Property<byte[]>(cart, "RowVersion"))
            .FirstAsync(cancellationToken);
    }

    public async Task<(T Result, byte[] NewRowVersion)> ExecuteWithLockAsync<T>(
        Guid userId,
        Func<Cart, byte[], Task<T>> action,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var cart = await _dbContext.Carts
            .FromSqlInterpolated($"SELECT * FROM Carts WITH (UPDLOCK) WHERE UserId = {userId}")
            .Include(c => c.Items)
            .FirstOrDefaultAsync(cancellationToken);

        if (cart is null)
        {
            throw new NotFoundException(
                $"Cart for user {userId} was not found.",
                "cart_not_found");
        }

        var rowVersion = _dbContext.Entry(cart).Property<byte[]>("RowVersion").CurrentValue;

        var result = await action(cart, rowVersion);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var newRowVersion = await _dbContext.Carts
            .AsNoTracking()
            .Where(c => c.Id == cart.Id)
            .Select(c => EF.Property<byte[]>(c, "RowVersion"))
            .FirstAsync(cancellationToken);

        return (result, newRowVersion);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        return exception.InnerException is Microsoft.Data.SqlClient.SqlException sqlException
            && (sqlException.Number == 2601 || sqlException.Number == 2627);
    }
}

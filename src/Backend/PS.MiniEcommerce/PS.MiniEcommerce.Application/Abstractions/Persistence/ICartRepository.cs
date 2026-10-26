using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Application.Abstractions.Persistence;

public interface ICartRepository
{
    Task<Cart?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<Cart> CreateAsync(Guid userId, DateTime utcNow, CancellationToken cancellationToken);

    Task<byte[]> GetRowVersionAsync(Guid cartId, CancellationToken cancellationToken);

    Task<(T Result, byte[] NewRowVersion)> ExecuteWithLockAsync<T>(
        Guid userId,
        Func<Cart, byte[], Task<T>> action,
        CancellationToken cancellationToken);
}

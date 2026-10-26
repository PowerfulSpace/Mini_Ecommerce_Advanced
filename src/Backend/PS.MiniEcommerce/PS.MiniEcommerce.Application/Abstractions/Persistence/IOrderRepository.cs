using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Application.Abstractions.Persistence;

public interface IOrderRepository
{
    Task<(IReadOnlyList<Order> Items, int TotalCount)> GetPagedByUserIdAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<Order?> GetByIdWithItemsAsync(
        Guid orderId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<(CancelOrderResult Result, Order? Order)> CancelAsync(
        Guid orderId,
        Guid userId,
        CancellationToken cancellationToken);
}

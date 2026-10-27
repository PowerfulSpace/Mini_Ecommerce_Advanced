using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Application.Abstractions.Persistence;

public interface IOrderRepository
{
    Task<(IReadOnlyList<Order> Items, int TotalCount)> GetPagedByUserIdAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<(Order? Order, byte[]? RowVersion)> GetByIdWithItemsAsync(
        Guid orderId,
        Guid userId,
        CancellationToken cancellationToken);

    Task<byte[]?> GetRowVersionAsync(Guid orderId, CancellationToken cancellationToken);

    Task<(CancelOrderResult Result, Order? Order, OrderStatus? CurrentStatus, byte[]? NewRowVersion)> CancelAsync(
        Guid orderId,
        Guid userId,
        string ifMatch,
        IReadOnlyList<(Guid OrderItemId, int Quantity)>? items,
        CancellationToken cancellationToken);

    Task ConsumeReservedAsync(Guid orderId, CancellationToken cancellationToken);
}

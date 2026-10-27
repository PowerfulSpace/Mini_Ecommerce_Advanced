using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Orders;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Infrastructure.Persistence;

public class OrderRepository : IOrderRepository
{
    private enum CancellationStockAction
    {
        None,
        ReleaseReservation,
        RestoreAvailable
    }

    private readonly AppDbContext _dbContext;

    public OrderRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<(IReadOnlyList<Order> Items, int TotalCount)> GetPagedByUserIdAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.Orders
            .AsNoTracking()
            .Where(order => order.UserId == userId);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(order => order.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<(Order? Order, byte[]? RowVersion)> GetByIdWithItemsAsync(
        Guid orderId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders
            .Include(order => order.Items)
            .ThenInclude(item => item.Cancellations)
            .FirstOrDefaultAsync(
                order => order.Id == orderId && order.UserId == userId,
                cancellationToken);

        if (order is null)
        {
            return (null, null);
        }

        var rowVersion = _dbContext.Entry(order).Property<byte[]>("RowVersion").CurrentValue;

        return (order, rowVersion);
    }

    public async Task<byte[]?> GetRowVersionAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return await _dbContext.Orders
            .AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(order => EF.Property<byte[]>(order, "RowVersion"))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<(CancelOrderResult Result, Order? Order, OrderStatus? CurrentStatus, byte[]? NewRowVersion, IReadOnlyList<object> DomainEvents)> CancelAsync(
        Guid orderId,
        Guid userId,
        string ifMatch,
        IReadOnlyList<(Guid OrderItemId, int Quantity)>? items,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var order = await _dbContext.Orders
            .FromSqlInterpolated(
                $"SELECT * FROM Orders WITH (UPDLOCK) WHERE Id = {orderId} AND UserId = {userId}")
            .Include(order => order.Items)
            .ThenInclude(item => item.Cancellations)
            .FirstOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (CancelOrderResult.NotFound, null, null, null, Array.Empty<object>());
        }

        var currentRowVersion = _dbContext.Entry(order).Property<byte[]>("RowVersion").CurrentValue;
        OrderVersionGuard.Validate(ifMatch, currentRowVersion);

        var currentStatus = order.Status;

        if (currentStatus == OrderStatus.Cancelled)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (CancelOrderResult.AlreadyCancelled, null, currentStatus, null, Array.Empty<object>());
        }

        if (currentStatus != OrderStatus.Created && currentStatus != OrderStatus.Paid)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (CancelOrderResult.InvalidTransition, null, currentStatus, null, Array.Empty<object>());
        }

        var utcNow = DateTime.UtcNow;
        var stockQuantities = new Dictionary<Guid, int>();

        if (items is null)
        {
            foreach (var item in order.Items)
            {
                var remainingQuantity = item.Quantity - item.CancelledQuantity;
                if (remainingQuantity > 0)
                {
                    AddStockQuantity(stockQuantities, item.ProductId, remainingQuantity);
                }
            }

            order.Cancel(utcNow);
        }
        else
        {
            foreach (var cancellation in items)
            {
                var orderItem = order.Items.FirstOrDefault(item => item.Id == cancellation.OrderItemId);

                if (orderItem is null)
                {
                    throw new DomainValidationException(
                        $"Order item with id {cancellation.OrderItemId} was not found in the order.");
                }

                AddStockQuantity(stockQuantities, orderItem.ProductId, cancellation.Quantity);
            }

            order.CancelItems(items, utcNow);
        }

        var stockAction = currentStatus == OrderStatus.Created
            ? CancellationStockAction.ReleaseReservation
            : CancellationStockAction.RestoreAvailable;

        // Paid -> Cancelled does not trigger a refund here; stage 19 (ADR-008) will connect refunds.
        if (stockQuantities.Count > 0
            && !await TryAdjustStockForCancellationAsync(stockQuantities, stockAction, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new DomainConflictException(
                "Failed to restore stock for the cancelled order.",
                "conflict");
        }

        _dbContext.Entry(order).Property(entry => entry.Status).IsModified = true;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var newRowVersion = await _dbContext.Orders
            .AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(order => EF.Property<byte[]>(order, "RowVersion"))
            .FirstAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        var domainEvents = order.DomainEvents.ToList();
        order.ClearDomainEvents();

        _dbContext.ChangeTracker.Clear();

        return (CancelOrderResult.Success, order, currentStatus, newRowVersion, domainEvents);
    }

    public async Task ConsumeReservedAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var orderItems = await _dbContext.OrderItems
            .AsNoTracking()
            .Where(item => item.OrderId == orderId)
            .OrderBy(item => item.ProductId)
            .ToListAsync(cancellationToken);

        foreach (var item in orderItems)
        {
            var affected = await _dbContext.Products
                .Where(product =>
                    product.Id == item.ProductId
                    && product.Reserved >= item.Quantity)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        product => product.Reserved,
                        product => product.Reserved - item.Quantity),
                    cancellationToken);

            if (affected == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new DomainConflictException(
                    "Failed to consume reserved stock.",
                    "conflict");
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<bool> TryAdjustStockForCancellationAsync(
        IReadOnlyDictionary<Guid, int> quantities,
        CancellationStockAction stockAction,
        CancellationToken cancellationToken)
    {
        foreach (var (productId, quantity) in quantities.OrderBy(pair => pair.Key))
        {
            var affected = stockAction == CancellationStockAction.ReleaseReservation
                ? await _dbContext.Products
                    .Where(product =>
                        product.Id == productId
                        && product.Reserved >= quantity)
                    .ExecuteUpdateAsync(
                        setters => setters
                            .SetProperty(
                                product => product.Reserved,
                                product => product.Reserved - quantity)
                            .SetProperty(
                                product => product.Available,
                                product => product.Available + quantity),
                        cancellationToken)
                : await _dbContext.Products
                    .Where(product => product.Id == productId)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            product => product.Available,
                            product => product.Available + quantity),
                        cancellationToken);

            if (affected == 0)
            {
                return false;
            }
        }

        return true;
    }

    private static void AddStockQuantity(
        IDictionary<Guid, int> quantities,
        Guid productId,
        int quantity)
    {
        quantities.TryGetValue(productId, out var existingQuantity);
        quantities[productId] = existingQuantity + quantity;
    }
}

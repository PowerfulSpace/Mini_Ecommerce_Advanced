using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Infrastructure.Persistence;

public class OrderRepository : IOrderRepository
{
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

    public async Task<Order?> GetByIdWithItemsAsync(
        Guid orderId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Items)
            .FirstOrDefaultAsync(
                order => order.Id == orderId && order.UserId == userId,
                cancellationToken);
    }

    public async Task<(CancelOrderResult Result, Order? Order)> CancelAsync(
        Guid orderId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var affected = await _dbContext.Orders
            .Where(order =>
                order.Id == orderId
                && order.UserId == userId
                && order.Status == OrderStatus.Created)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(order => order.Status, OrderStatus.Cancelled),
                cancellationToken);

        if (affected == 0)
        {
            var existingStatus = await _dbContext.Orders
                .AsNoTracking()
                .Where(order => order.Id == orderId && order.UserId == userId)
                .Select(order => (OrderStatus?)order.Status)
                .FirstOrDefaultAsync(cancellationToken);

            await transaction.RollbackAsync(cancellationToken);

            if (existingStatus is null)
            {
                return (CancelOrderResult.NotFound, null);
            }

            return (CancelOrderResult.AlreadyCancelled, null);
        }

        var orderItems = await _dbContext.OrderItems
            .AsNoTracking()
            .Where(item => item.OrderId == orderId)
            .OrderBy(item => item.ProductId)
            .ToListAsync(cancellationToken);

        foreach (var item in orderItems)
        {
            await _dbContext.Products
                .Where(product => product.Id == item.ProductId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        product => product.StockQuantity,
                        product => product.StockQuantity + item.Quantity),
                    cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        _dbContext.ChangeTracker.Clear();

        var cancelledOrder = await _dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Items)
            .FirstAsync(order => order.Id == orderId, cancellationToken);

        return (CancelOrderResult.Success, cancelledOrder);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PS.MiniEcommerce.Application.Abstractions.Outbox;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Orders;
using PS.MiniEcommerce.Application.Payments;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Domain.Exceptions;
using PS.MiniEcommerce.Domain.Payments;

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
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentRefundService _paymentRefundService;
    private readonly IOutboxRepository _outboxRepository;

    public OrderRepository(
        AppDbContext dbContext,
        IPaymentRepository paymentRepository,
        IPaymentRefundService paymentRefundService,
        IOutboxRepository outboxRepository)
    {
        _dbContext = dbContext;
        _paymentRepository = paymentRepository;
        _paymentRefundService = paymentRefundService;
        _outboxRepository = outboxRepository;
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
            .Include(order => order.Payment)
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

    public async Task<Guid?> GetOrderUserIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return await _dbContext.Orders
            .AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(order => (Guid?)order.UserId)
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
        PaymentRefunded? paymentRefunded = null;

        var payment = currentStatus == OrderStatus.Paid
            ? await GetPaymentForRefundAsync(order.PaymentId, cancellationToken)
            : null;

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

        if (payment is not null)
        {
            var refundAmount = order.Status == OrderStatus.Cancelled
                ? payment.Amount - payment.RefundedAmount
                : CalculatePartialRefundAmount(order, items!);

            if (refundAmount.Amount > 0)
            {
                try
                {
                    await _paymentRefundService.RefundAsync(
                        payment,
                        refundAmount,
                        reason: null,
                        utcNow,
                        cancellationToken);

                    paymentRefunded = new PaymentRefunded(
                        payment.Id,
                        payment.OrderId,
                        refundAmount,
                        utcNow);
                }
                catch
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _dbContext.ChangeTracker.Clear();
                    throw;
                }
            }
        }

        var stockAction = currentStatus == OrderStatus.Created
            ? CancellationStockAction.ReleaseReservation
            : CancellationStockAction.RestoreAvailable;

        if (stockQuantities.Count > 0
            && !await TryAdjustStockForCancellationAsync(stockQuantities, stockAction, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new DomainConflictException(
                "Failed to restore stock for the cancelled order.",
                "conflict");
        }

        var domainEvents = order.DomainEvents.ToList();
        if (paymentRefunded is not null)
        {
            domainEvents.Add(paymentRefunded);
        }

        foreach (var domainEvent in domainEvents)
        {
            await _outboxRepository.AddAsync(domainEvent, utcNow, cancellationToken);
        }

        _dbContext.Entry(order).Property(entry => entry.Status).IsModified = true;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var newRowVersion = await _dbContext.Orders
            .AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(order => EF.Property<byte[]>(order, "RowVersion"))
            .FirstAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        order.ClearDomainEvents();

        _dbContext.ChangeTracker.Clear();

        return (CancelOrderResult.Success, order, currentStatus, newRowVersion, domainEvents);
    }

    public Task<(OrderTransitionResult Result, Order? Order, OrderStatus? CurrentStatus, byte[]? NewRowVersion, IReadOnlyList<object> DomainEvents)> ShipAsync(
        Guid orderId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        return TransitionAsync(
            orderId,
            order => order.Ship(utcNow),
            utcNow,
            cancellationToken);
    }

    public Task<(OrderTransitionResult Result, Order? Order, OrderStatus? CurrentStatus, byte[]? NewRowVersion, IReadOnlyList<object> DomainEvents)> DeliverAsync(
        Guid orderId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        return TransitionAsync(
            orderId,
            order => order.Deliver(utcNow),
            utcNow,
            cancellationToken);
    }

    public async Task<(OrderTransitionResult Result, Order? Order, OrderStatus? CurrentStatus, byte[]? NewRowVersion, IReadOnlyList<object> DomainEvents)> ReturnAsync(
        Guid orderId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var order = await GetOrderForUpdateAsync(orderId, cancellationToken);

        if (order is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (OrderTransitionResult.NotFound, null, null, null, Array.Empty<object>());
        }

        var currentStatus = order.Status;

        try
        {
            order.Return(utcNow);
        }
        catch (DomainConflictException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (OrderTransitionResult.InvalidTransition, null, currentStatus, null, Array.Empty<object>());
        }

        PaymentRefunded? paymentRefunded = null;

        try
        {
            var payment = await GetPaymentForRefundAsync(order.PaymentId, cancellationToken);
            var refundAmount = payment.Amount - payment.RefundedAmount;

            if (refundAmount.Amount > 0)
            {
                await _paymentRefundService.RefundAsync(
                    payment,
                    refundAmount,
                    reason: null,
                    utcNow,
                    cancellationToken);

                paymentRefunded = new PaymentRefunded(
                    payment.Id,
                    payment.OrderId,
                    refundAmount,
                    utcNow);
            }
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        if (!await TryRestoreAvailableForReturnAsync(order, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new DomainConflictException(
                "Failed to restore stock for the returned order.",
                "conflict");
        }

        var domainEvents = order.DomainEvents.ToList();
        if (paymentRefunded is not null)
        {
            domainEvents.Add(paymentRefunded);
        }

        foreach (var domainEvent in domainEvents)
        {
            await _outboxRepository.AddAsync(domainEvent, utcNow, cancellationToken);
        }

        _dbContext.Entry(order).Property(entry => entry.Status).IsModified = true;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var newRowVersion = await GetCurrentRowVersionAsync(orderId, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        order.ClearDomainEvents();

        _dbContext.ChangeTracker.Clear();

        return (OrderTransitionResult.Success, order, currentStatus, newRowVersion, domainEvents);
    }

    public async Task ConsumeReservedAsync(
        Guid orderId,
        CancellationToken cancellationToken,
        bool useOwnTransaction = true)
    {
        IDbContextTransaction? transaction = null;

        if (useOwnTransaction)
        {
            transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        try
        {
            var orderItems = await _dbContext.OrderItems
                .AsNoTracking()
                .Include(item => item.Cancellations)
                .Where(item => item.OrderId == orderId)
                .OrderBy(item => item.ProductId)
                .ToListAsync(cancellationToken);

            foreach (var item in orderItems)
            {
                var quantity = item.Quantity - item.CancelledQuantity;

                if (quantity <= 0)
                {
                    continue;
                }

                var affected = await _dbContext.Products
                    .Where(product =>
                        product.Id == item.ProductId
                        && product.Reserved >= quantity)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            product => product.Reserved,
                            product => product.Reserved - quantity),
                        cancellationToken);

                if (affected == 0)
                {
                    if (transaction is not null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                    }

                    throw new DomainConflictException(
                        "Failed to consume reserved stock.",
                        "conflict");
                }
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private async Task<(OrderTransitionResult Result, Order? Order, OrderStatus? CurrentStatus, byte[]? NewRowVersion, IReadOnlyList<object> DomainEvents)> TransitionAsync(
        Guid orderId,
        Action<Order> transition,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var order = await GetOrderForUpdateAsync(orderId, cancellationToken);

        if (order is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (OrderTransitionResult.NotFound, null, null, null, Array.Empty<object>());
        }

        var currentStatus = order.Status;

        try
        {
            transition(order);
        }
        catch (DomainConflictException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (OrderTransitionResult.InvalidTransition, null, currentStatus, null, Array.Empty<object>());
        }

        var domainEvents = order.DomainEvents.ToList();

        foreach (var domainEvent in domainEvents)
        {
            await _outboxRepository.AddAsync(domainEvent, utcNow, cancellationToken);
        }

        _dbContext.Entry(order).Property(entry => entry.Status).IsModified = true;

        await _dbContext.SaveChangesAsync(cancellationToken);

        var newRowVersion = await GetCurrentRowVersionAsync(orderId, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        order.ClearDomainEvents();

        _dbContext.ChangeTracker.Clear();

        return (OrderTransitionResult.Success, order, currentStatus, newRowVersion, domainEvents);
    }

    private async Task<Order?> GetOrderForUpdateAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Orders
            .FromSqlInterpolated($"SELECT * FROM Orders WITH (UPDLOCK) WHERE Id = {orderId}")
            .Include(order => order.Items)
            .ThenInclude(item => item.Cancellations)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<byte[]> GetCurrentRowVersionAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Orders
            .AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(order => EF.Property<byte[]>(order, "RowVersion"))
            .FirstAsync(cancellationToken);
    }

    private async Task<bool> TryRestoreAvailableForReturnAsync(
        Order order,
        CancellationToken cancellationToken)
    {
        var quantities = order.Items
            .Select(item => (item.ProductId, Quantity: item.Quantity - item.CancelledQuantity))
            .Where(item => item.Quantity > 0)
            .GroupBy(item => item.ProductId)
            .Select(group => (ProductId: group.Key, Quantity: group.Sum(item => item.Quantity)))
            .OrderBy(item => item.ProductId);

        foreach (var (productId, quantity) in quantities)
        {
            var affected = await _dbContext.Products
                .Where(product => product.Id == productId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        product => product.Available,
                        product => product.Available + quantity),
                    cancellationToken);

            if (affected != 1)
            {
                return false;
            }
        }

        return true;
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

    private async Task<Payment> GetPaymentForRefundAsync(
        Guid? paymentId,
        CancellationToken cancellationToken)
    {
        if (paymentId is null)
        {
            throw new DomainConflictException(
                "A payment is required to refund the order.",
                "refund_not_allowed");
        }

        var payment = await _paymentRepository.GetByIdAsync(paymentId.Value, cancellationToken);

        if (payment is null)
        {
            throw new DomainConflictException(
                "A payment is required to refund the order.",
                "refund_not_allowed");
        }

        return payment;
    }

    private static Money CalculatePartialRefundAmount(
        Order order,
        IReadOnlyList<(Guid OrderItemId, int Quantity)> items)
    {
        var amount = Money.Zero;

        foreach (var cancellation in items)
        {
            var orderItem = order.Items.First(item => item.Id == cancellation.OrderItemId);
            amount += orderItem.UnitPrice * cancellation.Quantity;
        }

        return amount;
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

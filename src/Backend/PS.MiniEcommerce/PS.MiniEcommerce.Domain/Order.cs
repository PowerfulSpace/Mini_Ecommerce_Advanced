using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Domain.Exceptions;
using PS.MiniEcommerce.Domain.Payments;

namespace PS.MiniEcommerce.Domain;

public class Order
{
    private readonly List<OrderItem> _items = new();
    private readonly List<object> _domainEvents = new();

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public OrderStatus Status { get; private set; }

    public Guid? PaymentId { get; private set; }

    public Payment? Payment { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public Money TotalAmount { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    public IReadOnlyCollection<object> DomainEvents => _domainEvents.AsReadOnly();

    private Order()
    {
    }

    private Order(Guid id, Guid userId, DateTime utcNow, Guid? paymentId)
    {
        Id = id;
        UserId = userId;
        Status = OrderStatus.Created;
        PaymentId = paymentId;
        CreatedAtUtc = utcNow;
    }

    public static Order Create(
        Guid userId,
        IEnumerable<OrderItemData> items,
        DateTime utcNow,
        Guid? paymentId = null)
    {
        if (items is null)
        {
            throw new DomainValidationException("Items must not be null.");
        }

        var dataList = items.ToList();
        if (dataList.Count == 0)
        {
            throw new DomainValidationException("Order must contain at least one item.");
        }

        var order = new Order(Guid.NewGuid(), userId, utcNow, paymentId);

        foreach (var data in dataList)
        {
            order._items.Add(new OrderItem(order.Id, data));
        }

        order.TotalAmount = order._items.Aggregate(
            Money.Zero,
            (total, item) => total + item.UnitPrice * item.Quantity);

        order.RaiseDomainEvent(new OrderPlaced(
            order.Id,
            order.UserId,
            order.TotalAmount,
            order._items.Count,
            utcNow));

        return order;
    }

    public void SetPaymentId(Guid paymentId)
    {
        if (PaymentId is not null)
        {
            throw new DomainConflictException(
                "Order already has a payment.",
                "invalid_order_transition");
        }

        PaymentId = paymentId;
    }

    public void SetPayment(Payment payment)
    {
        if (payment is null)
        {
            throw new DomainValidationException("Payment must not be null.");
        }

        if (payment.OrderId != Id)
        {
            throw new DomainValidationException("Payment must belong to the order.");
        }

        if (PaymentId is not null && PaymentId != payment.Id)
        {
            throw new DomainConflictException(
                "Order already has a different payment.",
                "invalid_order_transition");
        }

        Payment = payment;
        PaymentId = payment.Id;
    }

    public void MarkPaid()
    {
        MarkPaid(DateTime.UtcNow);
    }

    public void MarkPaid(DateTime utcNow)
    {
        EnsureTransition(OrderStatus.Created, OrderStatus.Paid);

        if (PaymentId is null)
        {
            throw new DomainConflictException(
                $"Cannot transition order from '{Status}' to '{OrderStatus.Paid}': payment is required.",
                "invalid_order_transition");
        }

        Status = OrderStatus.Paid;
        RaiseDomainEvent(new OrderPaid(Id, UserId, utcNow));
    }

    public void Ship()
    {
        Ship(DateTime.UtcNow);
    }

    public void Ship(DateTime utcNow)
    {
        EnsureTransition(OrderStatus.Paid, OrderStatus.Shipped);
        Status = OrderStatus.Shipped;
        RaiseDomainEvent(new OrderShipped(Id, UserId, utcNow));
    }

    public void Deliver()
    {
        Deliver(DateTime.UtcNow);
    }

    public void Deliver(DateTime utcNow)
    {
        EnsureTransition(OrderStatus.Shipped, OrderStatus.Delivered);
        Status = OrderStatus.Delivered;
        RaiseDomainEvent(new OrderDelivered(Id, UserId, utcNow));
    }

    public void Cancel()
    {
        Cancel(DateTime.UtcNow);
    }

    public void Cancel(DateTime utcNow)
    {
        var remainingItems = _items
            .Select(item => (item.Id, item.Quantity - item.CancelledQuantity))
            .Where(item => item.Item2 > 0)
            .ToList();

        CancelItems(remainingItems, utcNow);
    }

    public void CancelItems(
        IReadOnlyList<(Guid OrderItemId, int Quantity)> items,
        DateTime utcNow,
        string? reason = null)
    {
        EnsureCancellable();

        if (items is null)
        {
            throw new DomainValidationException("Cancellation items must not be null.");
        }

        if (items.Count == 0)
        {
            throw new DomainValidationException("Cancellation items must not be empty.");
        }

        if (items.Select(item => item.OrderItemId).Distinct().Count() != items.Count)
        {
            throw new DomainValidationException("Cancellation items must not contain duplicate order item ids.");
        }

        var targets = new List<(OrderItem Item, int Quantity)>(items.Count);

        foreach (var cancellation in items)
        {
            var item = _items.FirstOrDefault(orderItem => orderItem.Id == cancellation.OrderItemId);

            if (item is null)
            {
                throw new DomainValidationException(
                    $"Order item with id {cancellation.OrderItemId} was not found in the order.");
            }

            targets.Add((item, cancellation.Quantity));
        }

        foreach (var target in targets)
        {
            target.Item.AddCancellation(target.Quantity, utcNow, reason);
        }

        if (_items.All(item => item.CancelledQuantity == item.Quantity))
        {
            Status = OrderStatus.Cancelled;
        }

        var cancelledItems = targets
            .Select(target => new CancelledItem(target.Item.Id, target.Item.ProductId, target.Quantity))
            .ToList();

        RaiseDomainEvent(new OrderCancelled(
            Id,
            UserId,
            Status == OrderStatus.Cancelled,
            cancelledItems,
            utcNow));
    }

    public void Return()
    {
        Return(DateTime.UtcNow);
    }

    public void Return(DateTime utcNow)
    {
        EnsureTransition(OrderStatus.Delivered, OrderStatus.Returned);
        Status = OrderStatus.Returned;
        RaiseDomainEvent(new OrderReturned(Id, UserId, utcNow));
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }

    private void RaiseDomainEvent(object domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    private void EnsureCancellable()
    {
        if (Status != OrderStatus.Created && Status != OrderStatus.Paid)
        {
            throw new DomainConflictException(
                $"Cannot transition order from '{Status}' to '{OrderStatus.Cancelled}'.",
                "invalid_order_transition");
        }
    }

    private void EnsureTransition(OrderStatus expected, OrderStatus requested)
    {
        if (Status != expected)
        {
            throw new DomainConflictException(
                $"Cannot transition order from '{Status}' to '{requested}'.",
                "invalid_order_transition");
        }
    }
}

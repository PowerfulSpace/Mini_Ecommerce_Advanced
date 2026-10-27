using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Domain;

public class Order
{
    private readonly List<OrderItem> _items = new();

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public OrderStatus Status { get; private set; }

    public Guid? PaymentId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public Money TotalAmount { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

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

        return order;
    }

    public void MarkPaid()
    {
        EnsureTransition(OrderStatus.Created, OrderStatus.Paid);

        if (PaymentId is null)
        {
            throw new DomainConflictException(
                $"Cannot transition order from '{Status}' to '{OrderStatus.Paid}': payment is required.",
                "invalid_order_transition");
        }

        Status = OrderStatus.Paid;
    }

    public void Ship()
    {
        EnsureTransition(OrderStatus.Paid, OrderStatus.Shipped);
        Status = OrderStatus.Shipped;
    }

    public void Deliver()
    {
        EnsureTransition(OrderStatus.Shipped, OrderStatus.Delivered);
        Status = OrderStatus.Delivered;
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
    }

    public void Return()
    {
        EnsureTransition(OrderStatus.Delivered, OrderStatus.Returned);
        Status = OrderStatus.Returned;
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

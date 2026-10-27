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
        if (Status != OrderStatus.Created && Status != OrderStatus.Paid)
        {
            throw new DomainConflictException(
                $"Cannot transition order from '{Status}' to '{OrderStatus.Cancelled}'.",
                "invalid_order_transition");
        }

        Status = OrderStatus.Cancelled;
    }

    public void Return()
    {
        EnsureTransition(OrderStatus.Delivered, OrderStatus.Returned);
        Status = OrderStatus.Returned;
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

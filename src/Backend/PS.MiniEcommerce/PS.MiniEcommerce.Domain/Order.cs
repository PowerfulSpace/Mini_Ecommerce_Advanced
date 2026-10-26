using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Domain;

public class Order
{
    private readonly List<OrderItem> _items = new();

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public OrderStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public decimal TotalAmount { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    private Order()
    {
    }

    private Order(Guid id, Guid userId, DateTime utcNow)
    {
        Id = id;
        UserId = userId;
        Status = OrderStatus.Created;
        CreatedAtUtc = utcNow;
    }

    public static Order Create(Guid userId, IEnumerable<OrderItemData> items, DateTime utcNow)
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

        var order = new Order(Guid.NewGuid(), userId, utcNow);

        foreach (var data in dataList)
        {
            order._items.Add(new OrderItem(order.Id, data));
        }

        order.TotalAmount = order._items.Sum(item => item.UnitPrice * item.Quantity);

        return order;
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Cancelled)
        {
            throw new DomainConflictException("Order is already cancelled.");
        }

        Status = OrderStatus.Cancelled;
    }
}

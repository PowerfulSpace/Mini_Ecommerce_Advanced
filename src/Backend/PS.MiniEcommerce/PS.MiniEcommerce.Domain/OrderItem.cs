using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Domain;

public class OrderItem
{
    private const int MaxProductNameLength = 200;

    public Guid Id { get; private set; }

    public Guid OrderId { get; private set; }

    public Guid ProductId { get; private set; }

    public string ProductName { get; private set; }

    public decimal UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    private OrderItem()
    {
        ProductName = string.Empty;
    }

    internal OrderItem(Guid orderId, OrderItemData data)
    {
        if (string.IsNullOrWhiteSpace(data.ProductName))
        {
            throw new DomainValidationException("Product name must not be empty.");
        }

        if (data.ProductName.Length > MaxProductNameLength)
        {
            throw new DomainValidationException("Product name must not exceed 200 characters.");
        }

        if (data.UnitPrice < 0)
        {
            throw new DomainValidationException("Unit price must not be negative.");
        }

        if (data.Quantity <= 0)
        {
            throw new DomainValidationException("Quantity must be positive.");
        }

        Id = Guid.NewGuid();
        OrderId = orderId;
        ProductId = data.ProductId;
        ProductName = data.ProductName;
        UnitPrice = data.UnitPrice;
        Quantity = data.Quantity;
    }
}

namespace PS.MiniEcommerce.Domain;

public class CartItem
{
    public Guid Id { get; private set; }

    public Guid CartId { get; private set; }

    public Guid ProductId { get; private set; }

    public int Quantity { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    private CartItem()
    {
    }

    internal CartItem(Guid cartId, Guid productId, int quantity, DateTime utcNow)
    {
        Id = Guid.NewGuid();
        CartId = cartId;
        ProductId = productId;
        Quantity = quantity;
        CreatedAtUtc = utcNow;
        UpdatedAtUtc = utcNow;
    }

    internal void IncreaseQuantity(int delta, DateTime utcNow)
    {
        Quantity += delta;
        UpdatedAtUtc = utcNow;
    }

    internal void SetQuantity(int quantity, DateTime utcNow)
    {
        Quantity = quantity;
        UpdatedAtUtc = utcNow;
    }
}

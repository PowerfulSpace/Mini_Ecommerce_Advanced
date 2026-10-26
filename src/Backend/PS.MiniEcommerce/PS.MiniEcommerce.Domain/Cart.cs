using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Domain;

public class Cart
{
    private readonly List<CartItem> _items = new();

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<CartItem> Items => _items;

    private Cart(Guid id, Guid userId, DateTime createdAtUtc)
    {
        Id = id;
        UserId = userId;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public static Cart Create(Guid userId, DateTime utcNow)
    {
        return new Cart(Guid.NewGuid(), userId, utcNow);
    }

    public void AddItem(Guid productId, int quantity, DateTime utcNow)
    {
        if (quantity <= 0)
        {
            throw new DomainValidationException("Quantity must be positive.");
        }

        var existingItem = _items.Find(item => item.ProductId == productId);
        if (existingItem is not null)
        {
            existingItem.IncreaseQuantity(quantity, utcNow);
        }
        else
        {
            _items.Add(new CartItem(Id, productId, quantity, utcNow));
        }

        UpdatedAtUtc = utcNow;
    }

    public void UpdateItemQuantity(Guid productId, int quantity, DateTime utcNow)
    {
        if (quantity <= 0)
        {
            throw new DomainValidationException("Quantity must be positive.");
        }

        var existingItem = _items.Find(item => item.ProductId == productId);
        if (existingItem is null)
        {
            throw new DomainConflictException("Cart item not found.");
        }

        existingItem.SetQuantity(quantity, utcNow);
        UpdatedAtUtc = utcNow;
    }

    public void RemoveItem(Guid productId, DateTime utcNow)
    {
        var existingItem = _items.Find(item => item.ProductId == productId);
        if (existingItem is null)
        {
            throw new DomainConflictException("Cart item not found.");
        }

        _items.Remove(existingItem);
        UpdatedAtUtc = utcNow;
    }

    public void Clear(DateTime utcNow)
    {
        _items.Clear();
        UpdatedAtUtc = utcNow;
    }
}

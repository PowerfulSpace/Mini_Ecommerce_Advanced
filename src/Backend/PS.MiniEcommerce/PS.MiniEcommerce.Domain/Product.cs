using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Domain;

public class Product
{
    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 2000;

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    public decimal Price { get; private set; }

    public int StockQuantity { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    private Product(Guid id, string name, string description, decimal price, int stockQuantity, DateTime createdAtUtc)
    {
        Id = id;
        Name = name;
        Description = description;
        Price = price;
        StockQuantity = stockQuantity;
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
    }

    public static Product Create(string name, string description, decimal price, int stockQuantity, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainValidationException("Name must not be empty.");
        }

        if (name.Length > MaxNameLength)
        {
            throw new DomainValidationException("Name must not exceed 200 characters.");
        }

        if (description is null)
        {
            throw new DomainValidationException("Description must not be null.");
        }

        if (description.Length > MaxDescriptionLength)
        {
            throw new DomainValidationException("Description must not exceed 2000 characters.");
        }

        if (price < 0)
        {
            throw new DomainValidationException("Price must not be negative.");
        }

        if (stockQuantity < 0)
        {
            throw new DomainValidationException("Stock quantity must not be negative.");
        }

        return new Product(Guid.NewGuid(), name, description, price, stockQuantity, utcNow);
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void DeductStock(int quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainValidationException("Quantity must be positive.");
        }

        if (quantity > StockQuantity)
        {
            throw new DomainConflictException("Insufficient stock.");
        }

        StockQuantity -= quantity;
    }

    public void RestoreStock(int quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainValidationException("Quantity must be positive.");
        }

        StockQuantity += quantity;
    }
}

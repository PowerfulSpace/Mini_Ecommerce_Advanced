using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Domain;

public class Product
{
    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 2000;

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    public Money Price { get; private set; }

    public int Available { get; private set; }

    public int Reserved { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    private Product(
        Guid id,
        string name,
        string description,
        Money price,
        int available,
        int reserved,
        DateTime createdAtUtc)
    {
        Id = id;
        Name = name;
        Description = description;
        Price = price;
        Available = available;
        Reserved = reserved;
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
    }

    public static Product Create(
        string name,
        string description,
        Money price,
        int available,
        DateTime utcNow,
        int reserved = 0)
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

        if (price.Amount < 0)
        {
            throw new DomainValidationException("Price must not be negative.");
        }

        if (available < 0)
        {
            throw new DomainValidationException("Available quantity must not be negative.");
        }

        if (reserved < 0)
        {
            throw new DomainValidationException("Reserved quantity must not be negative.");
        }

        return new Product(Guid.NewGuid(), name, description, price, available, reserved, utcNow);
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Reserve(int quantity)
    {
        EnsurePositiveQuantity(quantity);

        if (quantity > Available)
        {
            throw new DomainConflictException("Insufficient stock.");
        }

        Available -= quantity;
        Reserved += quantity;
        EnsureStockInvariants();
    }

    public void Release(int quantity)
    {
        EnsurePositiveQuantity(quantity);

        if (quantity > Reserved)
        {
            throw new DomainConflictException("Insufficient reserved stock.");
        }

        Reserved -= quantity;
        Available += quantity;
        EnsureStockInvariants();
    }

    public void Consume(int quantity)
    {
        EnsurePositiveQuantity(quantity);

        if (quantity > Reserved)
        {
            throw new DomainConflictException("Insufficient reserved stock.");
        }

        Reserved -= quantity;
        EnsureStockInvariants();
    }

    public void RestoreAvailable(int quantity)
    {
        EnsurePositiveQuantity(quantity);

        Available += quantity;
        EnsureStockInvariants();
    }

    public void AdjustAvailable(int newAvailable)
    {
        if (newAvailable < 0)
        {
            throw new DomainValidationException("Available quantity must not be negative.");
        }

        Available = newAvailable;
        EnsureStockInvariants();
    }

    private static void EnsurePositiveQuantity(int quantity)
    {
        if (quantity <= 0)
        {
            throw new DomainValidationException("Quantity must be positive.");
        }
    }

    private void EnsureStockInvariants()
    {
        if (Available < 0)
        {
            throw new DomainValidationException("Available quantity must not be negative.");
        }

        if (Reserved < 0)
        {
            throw new DomainValidationException("Reserved quantity must not be negative.");
        }
    }
}

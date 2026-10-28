using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Domain;

public class User
{
    private const int MaxEmailLength = 320;

    public Guid Id { get; private set; }

    public string Email { get; private set; }

    public UserRole Role { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    private User(Guid id, string email, UserRole role, DateTime createdAtUtc)
    {
        Id = id;
        Email = email;
        Role = role;
        CreatedAtUtc = createdAtUtc;
    }

    public static User Create(string email, DateTime utcNow, UserRole role = UserRole.Customer)
    {
        return Create(email, role, utcNow);
    }

    public static User Create(string email, UserRole role, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new DomainValidationException("Email must not be empty.");
        }

        var normalizedEmail = email.Trim();

        if (normalizedEmail.Length > MaxEmailLength)
        {
            throw new DomainValidationException("Email must not exceed 320 characters.");
        }

        if (!normalizedEmail.Contains('@') || normalizedEmail.StartsWith('@') || normalizedEmail.EndsWith('@'))
        {
            throw new DomainValidationException("Email must be a valid email address.");
        }

        if (!Enum.IsDefined(typeof(UserRole), role))
        {
            throw new DomainValidationException("Role must be either customer or admin.");
        }

        return new User(Guid.NewGuid(), normalizedEmail, role, utcNow);
    }
}

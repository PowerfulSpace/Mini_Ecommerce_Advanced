using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Domain;

public class User
{
    private const int MaxEmailLength = 320;

    public Guid Id { get; private set; }

    public string Email { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    private User(Guid id, string email, DateTime createdAtUtc)
    {
        Id = id;
        Email = email;
        CreatedAtUtc = createdAtUtc;
    }

    public static User Create(string email, DateTime utcNow)
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

        return new User(Guid.NewGuid(), normalizedEmail, utcNow);
    }
}

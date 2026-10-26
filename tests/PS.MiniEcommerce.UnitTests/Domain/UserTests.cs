using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.UnitTests.Domain;

public class UserTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithValidEmail_SetsProperties()
    {
        var user = User.Create("alice@example.com", FixedUtcNow);

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("alice@example.com", user.Email);
        Assert.Equal(FixedUtcNow, user.CreatedAtUtc);
    }

    [Fact]
    public void Create_WithEmptyEmail_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(() => User.Create(string.Empty, FixedUtcNow));
    }

    [Fact]
    public void Create_WithWhitespaceEmail_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(() => User.Create("   ", FixedUtcNow));
    }

    [Fact]
    public void Create_WithNullEmail_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(() => User.Create(null!, FixedUtcNow));
    }

    [Fact]
    public void Create_WithEmailWithoutAtSign_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(() => User.Create("alice.example.com", FixedUtcNow));
    }

    [Fact]
    public void Create_WithEmailLongerThan320Characters_ThrowsDomainValidationException()
    {
        var email = new string('a', 320) + "@example.com";

        Assert.Throws<DomainValidationException>(() => User.Create(email, FixedUtcNow));
    }

    [Fact]
    public void Create_WithEmailStartingWithAtSign_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(() => User.Create("@example.com", FixedUtcNow));
    }

    [Fact]
    public void Create_WithEmailEndingWithAtSign_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(() => User.Create("example.com@", FixedUtcNow));
    }
}

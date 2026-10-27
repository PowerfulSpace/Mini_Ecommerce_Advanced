using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Application.Orders;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.UnitTests.Application.Orders;

public class OrderVersionGuardTests
{
    private static readonly byte[] RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
    private const string ValidIfMatch = "\"AQIDBAUGBwg=\"";

    [Fact]
    public void EnsurePresent_WithMissingValue_ThrowsPreconditionRequired()
    {
        var exception = Assert.Throws<DomainConflictException>(
            () => OrderVersionGuard.EnsurePresent(null));

        Assert.Equal("precondition_required", exception.Code);
    }

    [Fact]
    public void EnsurePresent_WithEmptyValue_ThrowsPreconditionRequired()
    {
        var exception = Assert.Throws<DomainConflictException>(
            () => OrderVersionGuard.EnsurePresent("   "));

        Assert.Equal("precondition_required", exception.Code);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("\"*\"")]
    public void EnsurePresent_WithWildcard_ThrowsValidationException(string ifMatch)
    {
        Assert.Throws<ValidationException>(() => OrderVersionGuard.EnsurePresent(ifMatch));
    }

    [Theory]
    [InlineData("\"not-base64\"")]
    [InlineData("\"\"")]
    public void EnsurePresent_WithInvalidEntityTag_ThrowsValidationException(string ifMatch)
    {
        Assert.Throws<ValidationException>(() => OrderVersionGuard.EnsurePresent(ifMatch));
    }

    [Fact]
    public void Validate_WithMatchingValue_DoesNotThrow()
    {
        OrderVersionGuard.Validate(ValidIfMatch, RowVersion);
    }

    [Fact]
    public void Validate_WithStaleValue_ThrowsOrderVersionConflict()
    {
        var exception = Assert.Throws<DomainConflictException>(
            () => OrderVersionGuard.Validate("\"AAAAAAAAAAA=\"", RowVersion));

        Assert.Equal("order_version_conflict", exception.Code);
    }
}

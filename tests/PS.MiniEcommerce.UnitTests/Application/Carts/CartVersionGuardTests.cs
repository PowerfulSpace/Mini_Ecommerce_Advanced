using PS.MiniEcommerce.Application.Carts;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.UnitTests.Application.Carts;

public class CartVersionGuardTests
{
    private static readonly byte[] CurrentRowVersion = { 1, 2, 3, 4, 5, 6, 7, 8 };

    [Fact]
    public void Validate_WhenIfMatchIsNull_ThrowsDomainConflictWithPreconditionRequired()
    {
        var exception = Assert.Throws<DomainConflictException>(
            () => CartVersionGuard.Validate(null, CurrentRowVersion));

        Assert.Equal("precondition_required", exception.Code);
    }

    [Fact]
    public void Validate_WhenIfMatchIsEmpty_ThrowsDomainConflictWithPreconditionRequired()
    {
        var exception = Assert.Throws<DomainConflictException>(
            () => CartVersionGuard.Validate(string.Empty, CurrentRowVersion));

        Assert.Equal("precondition_required", exception.Code);
    }

    [Fact]
    public void Validate_WhenIfMatchIsWhitespace_ThrowsDomainConflictWithPreconditionRequired()
    {
        var exception = Assert.Throws<DomainConflictException>(
            () => CartVersionGuard.Validate("   ", CurrentRowVersion));

        Assert.Equal("precondition_required", exception.Code);
    }

    [Fact]
    public void Validate_WhenIfMatchIsWildcard_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(
            () => CartVersionGuard.Validate("*", CurrentRowVersion));
    }

    [Fact]
    public void Validate_WhenIfMatchIsNotBase64_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(
            () => CartVersionGuard.Validate("not-a-tag", CurrentRowVersion));
    }

    [Fact]
    public void Validate_WhenQuotedIfMatchIsNotBase64_ThrowsValidationException()
    {
        Assert.Throws<ValidationException>(
            () => CartVersionGuard.Validate("\"not-a-tag\"", CurrentRowVersion));
    }

    [Fact]
    public void Validate_WhenIfMatchIsStaleBase64_ThrowsDomainConflictWithCartVersionConflict()
    {
        var stale = Convert.ToBase64String(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 });

        var exception = Assert.Throws<DomainConflictException>(
            () => CartVersionGuard.Validate($"\"{stale}\"", CurrentRowVersion));

        Assert.Equal("cart_version_conflict", exception.Code);
    }

    [Fact]
    public void Validate_WhenIfMatchMatches_DoesNotThrow()
    {
        var current = Convert.ToBase64String(CurrentRowVersion);

        var exception = Record.Exception(
            () => CartVersionGuard.Validate($"\"{current}\"", CurrentRowVersion));

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_WhenIfMatchMatchesWithoutQuotes_DoesNotThrow()
    {
        var current = Convert.ToBase64String(CurrentRowVersion);

        var exception = Record.Exception(
            () => CartVersionGuard.Validate(current, CurrentRowVersion));

        Assert.Null(exception);
    }
}

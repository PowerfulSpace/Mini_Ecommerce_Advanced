using PS.MiniEcommerce.Application.Checkout.Commands;

namespace PS.MiniEcommerce.UnitTests.Application.Checkout;

public class CheckoutCommandValidatorTests
{
    private static readonly byte[] ValidFingerprint = new byte[32];
    private const string ValidIdempotencyKey = "valid-key-12345";
    private const string ValidIfMatch = "\"AAAAAAAAB9Q=\"";

    private readonly CheckoutCommandValidator _validator = new();

    [Fact]
    public void Validate_WithEmptyIdempotencyKey_IsInvalid()
    {
        var result = _validator.Validate(new CheckoutCommand(string.Empty, ValidIfMatch, ValidFingerprint));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "IdempotencyKey");
    }

    [Fact]
    public void Validate_WithIdempotencyKeyShorterThan8_IsInvalid()
    {
        var result = _validator.Validate(new CheckoutCommand("short", ValidIfMatch, ValidFingerprint));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "IdempotencyKey");
    }

    [Fact]
    public void Validate_WithIdempotencyKeyLongerThan128_IsInvalid()
    {
        var result = _validator.Validate(
            new CheckoutCommand(new string('a', 129), ValidIfMatch, ValidFingerprint));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "IdempotencyKey");
    }

    [Fact]
    public void Validate_WithIdempotencyKeyContainingInvalidChars_IsInvalid()
    {
        var result = _validator.Validate(new CheckoutCommand("invalid key!", ValidIfMatch, ValidFingerprint));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "IdempotencyKey");
    }

    [Fact]
    public void Validate_WithValidIdempotencyKey_IsValid()
    {
        var result = _validator.Validate(new CheckoutCommand(ValidIdempotencyKey, ValidIfMatch, ValidFingerprint));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithFingerprintNot32Bytes_IsInvalid()
    {
        var result = _validator.Validate(new CheckoutCommand(ValidIdempotencyKey, ValidIfMatch, new byte[16]));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "RequestFingerprint");
    }

    [Fact]
    public void Validate_WithValidFingerprint_IsValid()
    {
        var result = _validator.Validate(new CheckoutCommand(ValidIdempotencyKey, ValidIfMatch, new byte[32]));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithNullIfMatch_IsValid()
    {
        var result = _validator.Validate(new CheckoutCommand(ValidIdempotencyKey, null, ValidFingerprint));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithWildcardIfMatch_IsInvalid()
    {
        var result = _validator.Validate(new CheckoutCommand(ValidIdempotencyKey, "*", ValidFingerprint));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "IfMatch");
    }

    [Fact]
    public void Validate_WithIfMatchWithoutQuotes_IsInvalid()
    {
        var result = _validator.Validate(new CheckoutCommand(ValidIdempotencyKey, "AAAAAAAAB9Q=", ValidFingerprint));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "IfMatch");
    }

    [Fact]
    public void Validate_WithIfMatchNotBase64_IsInvalid()
    {
        var result = _validator.Validate(new CheckoutCommand(ValidIdempotencyKey, "\"not-base64!\"", ValidFingerprint));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == "IfMatch");
    }

    [Fact]
    public void Validate_WithValidIfMatch_IsValid()
    {
        var result = _validator.Validate(new CheckoutCommand(ValidIdempotencyKey, "\"AAAAAAAAB9Q=\"", ValidFingerprint));

        Assert.True(result.IsValid);
    }
}

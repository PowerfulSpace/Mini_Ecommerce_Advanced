using FluentValidation.Results;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Application.Carts;

public static class CartVersionGuard
{
    public static void Validate(string? ifMatch, byte[] currentRowVersion)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            throw new DomainConflictException(
                "The If-Match header is required for this operation.",
                "precondition_required");
        }

        var trimmed = ifMatch.Trim().Trim('"');

        if (trimmed.Length == 0 || string.Equals(trimmed, "*", StringComparison.Ordinal))
        {
            throw CreateValidationException("Wildcard If-Match is not supported.");
        }

        if (!IsBase64(trimmed))
        {
            throw CreateValidationException("If-Match must be a valid strong entity-tag.");
        }

        var currentVersion = Convert.ToBase64String(currentRowVersion);

        if (!string.Equals(trimmed, currentVersion, StringComparison.Ordinal))
        {
            throw new DomainConflictException(
                "The cart changed after it was retrieved. Fetch the latest cart and retry with an updated request.",
                "cart_version_conflict");
        }
    }

    private static bool IsBase64(string value)
    {
        var buffer = new byte[Math.Max(value.Length, 8)];
        return Convert.TryFromBase64String(value, buffer, out _);
    }

    private static ValidationException CreateValidationException(string message)
    {
        return new ValidationException(
            new[] { new ValidationFailure("If-Match", message) });
    }
}

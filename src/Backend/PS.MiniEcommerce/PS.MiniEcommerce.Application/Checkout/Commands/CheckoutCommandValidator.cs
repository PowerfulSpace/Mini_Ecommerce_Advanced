using System.Text.RegularExpressions;
using FluentValidation;

namespace PS.MiniEcommerce.Application.Checkout.Commands;

public partial class CheckoutCommandValidator : AbstractValidator<CheckoutCommand>
{
    [GeneratedRegex("^[A-Za-z0-9_-]{8,128}$")]
    private static partial Regex IdempotencyKeyRegex();

    public CheckoutCommandValidator()
    {
        RuleFor(command => command.IdempotencyKey)
            .NotEmpty()
            .Must(key => IdempotencyKeyRegex().IsMatch(key))
            .WithMessage("Idempotency-Key must match the pattern ^[A-Za-z0-9_-]{8,128}$.");

        RuleFor(command => command.RequestFingerprint)
            .NotNull()
            .Must(fingerprint => fingerprint is { Length: 32 })
            .WithMessage("Request fingerprint must be a 32-byte SHA-256 hash.");

        RuleFor(command => command.IfMatch)
            .Must(BeValidIfMatchFormat)
            .When(command => command.IfMatch is not null)
            .WithMessage("If-Match must be a strong entity-tag in the form \"<base64>\".");
    }

    private static bool BeValidIfMatchFormat(string? ifMatch)
    {
        if (ifMatch is null)
        {
            return true;
        }

        if (ifMatch.Length < 3)
        {
            return false;
        }

        if (!ifMatch.StartsWith('"') || !ifMatch.EndsWith('"'))
        {
            return false;
        }

        var inner = ifMatch[1..^1];

        return Convert.TryFromBase64String(inner, new byte[inner.Length], out _);
    }
}

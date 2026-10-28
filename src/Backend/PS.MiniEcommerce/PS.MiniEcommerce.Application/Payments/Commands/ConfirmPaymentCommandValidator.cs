using System.Text.RegularExpressions;
using FluentValidation;

namespace PS.MiniEcommerce.Application.Payments.Commands;

public partial class ConfirmPaymentCommandValidator : AbstractValidator<ConfirmPaymentCommand>
{
    [GeneratedRegex("^[A-Za-z0-9_-]{8,128}$")]
    private static partial Regex IdempotencyKeyRegex();

    public ConfirmPaymentCommandValidator()
    {
        RuleFor(command => command.PaymentId)
            .NotEmpty();

        RuleFor(command => command.IdempotencyKey)
            .NotEmpty()
            .Must(key => IdempotencyKeyRegex().IsMatch(key))
            .WithMessage("Idempotency-Key must match the pattern ^[A-Za-z0-9_-]{8,128}$.");

        RuleFor(command => command.RequestFingerprint)
            .NotNull()
            .Must(fingerprint => fingerprint is { Length: 32 })
            .WithMessage("Request fingerprint must be a 32-byte SHA-256 hash.");
    }
}

using MediatR;

namespace PS.MiniEcommerce.Application.Checkout.Commands;

public sealed record CheckoutCommand(
    string IdempotencyKey,
    string? IfMatch,
    byte[] RequestFingerprint)
    : IRequest<CheckoutExecutionResult>;

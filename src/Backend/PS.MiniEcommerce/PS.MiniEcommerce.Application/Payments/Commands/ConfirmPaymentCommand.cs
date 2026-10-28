using MediatR;

namespace PS.MiniEcommerce.Application.Payments.Commands;

public sealed record ConfirmPaymentCommand(
    Guid PaymentId,
    string IdempotencyKey,
    byte[] RequestFingerprint)
    : IRequest<ConfirmExecutionResult>;

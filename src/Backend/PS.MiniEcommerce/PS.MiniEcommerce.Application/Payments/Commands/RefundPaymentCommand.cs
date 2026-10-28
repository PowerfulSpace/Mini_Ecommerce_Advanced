using MediatR;

namespace PS.MiniEcommerce.Application.Payments.Commands;

public sealed record RefundPaymentCommand(
    Guid PaymentId,
    decimal Amount,
    string? Reason,
    string IdempotencyKey,
    byte[] RequestFingerprint)
    : IRequest<RefundExecutionResult>;

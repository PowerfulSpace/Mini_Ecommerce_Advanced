using PS.MiniEcommerce.Domain.Payments;

namespace PS.MiniEcommerce.Application.Payments.DTOs;

public sealed record PaymentDto(
    Guid Id,
    Guid OrderId,
    decimal Amount,
    decimal RefundedAmount,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? ConfirmedAtUtc,
    DateTime? RefundedAtUtc);

public static class PaymentDtoMapping
{
    public static PaymentDto ToDto(this Payment payment) => new(
        payment.Id,
        payment.OrderId,
        payment.Amount.Amount,
        payment.RefundedAmount.Amount,
        payment.Status.ToString(),
        payment.CreatedAtUtc,
        payment.ConfirmedAtUtc,
        payment.RefundedAtUtc);
}

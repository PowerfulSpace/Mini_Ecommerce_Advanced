using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Domain;

public class OrderItemCancellation
{
    private const int MaxReasonLength = 500;

    public Guid Id { get; private set; }

    public Guid OrderItemId { get; private set; }

    public int Quantity { get; private set; }

    public DateTime CancelledAtUtc { get; private set; }

    public string? Reason { get; private set; }

    private OrderItemCancellation()
    {
    }

    private OrderItemCancellation(
        Guid id,
        Guid orderItemId,
        int quantity,
        DateTime cancelledAtUtc,
        string? reason)
    {
        Id = id;
        OrderItemId = orderItemId;
        Quantity = quantity;
        CancelledAtUtc = cancelledAtUtc;
        Reason = reason;
    }

    public static OrderItemCancellation Create(
        Guid orderItemId,
        int quantity,
        DateTime utcNow,
        string? reason = null)
    {
        if (quantity <= 0)
        {
            throw new DomainValidationException("Cancellation quantity must be positive.");
        }

        if (reason is not null && reason.Length > MaxReasonLength)
        {
            throw new DomainValidationException(
                $"Cancellation reason must not exceed {MaxReasonLength} characters.");
        }

        return new OrderItemCancellation(
            Guid.NewGuid(),
            orderItemId,
            quantity,
            utcNow,
            reason);
    }
}

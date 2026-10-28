namespace PS.MiniEcommerce.Application.Payments;

public sealed class ConfirmAttemptContext
{
    public Guid? PaymentAttemptId { get; set; }

    public bool IsRetry { get; set; }
}

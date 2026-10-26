namespace PS.MiniEcommerce.Application.Checkout;

public interface ICheckoutTransactionExecutor
{
    Task<CheckoutExecutionResult> ExecuteAsync(
        Guid userId,
        Guid checkoutAttemptId,
        string? ifMatch,
        DateTime utcNow,
        CancellationToken cancellationToken);
}

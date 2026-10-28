using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Application.Payments;

public interface IRefundTransactionExecutor
{
    Task<RefundExecutionResult> ExecuteAsync(
        Guid paymentId,
        Money amount,
        string? reason,
        Guid paymentAttemptId,
        CancellationToken cancellationToken);
}

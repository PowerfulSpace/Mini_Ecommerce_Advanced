namespace PS.MiniEcommerce.Application.Payments;

public interface IConfirmTransactionExecutor
{
    Task<ConfirmExecutionResult> ExecuteAsync(
        Guid paymentId,
        Guid userId,
        CancellationToken cancellationToken);
}

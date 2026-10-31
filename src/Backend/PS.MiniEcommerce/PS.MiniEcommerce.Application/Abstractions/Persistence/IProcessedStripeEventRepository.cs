namespace PS.MiniEcommerce.Application.Abstractions.Persistence;

public interface IProcessedStripeEventRepository
{
    Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken);

    Task<bool> TryAddAsync(
        string eventId,
        string type,
        DateTime utcNow,
        CancellationToken cancellationToken);
}

namespace PS.MiniEcommerce.Application.Abstractions.Outbox;

public interface IOutboxSubscriber
{
    Task HandleAsync(object domainEvent, CancellationToken cancellationToken);
}

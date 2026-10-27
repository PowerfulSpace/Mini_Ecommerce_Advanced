using MediatR;

namespace PS.MiniEcommerce.Application.Events;

public sealed record DomainEventNotification<T>(T Event) : INotification where T : class;

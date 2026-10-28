using MediatR;
using Microsoft.Extensions.Logging;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Events;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Application.Orders.DTOs;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Application.Admin.Commands;

public class AdminDeliverOrderHandler : IRequestHandler<AdminDeliverOrderCommand, AdminOrderResult>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IMediator _mediator;
    private readonly ILogger<AdminDeliverOrderHandler> _logger;

    public AdminDeliverOrderHandler(
        IOrderRepository orderRepository,
        IMediator mediator,
        ILogger<AdminDeliverOrderHandler> logger)
    {
        _orderRepository = orderRepository;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<AdminOrderResult> Handle(
        AdminDeliverOrderCommand request,
        CancellationToken cancellationToken)
    {
        var (result, order, currentStatus, newRowVersion, domainEvents) =
            await _orderRepository.DeliverAsync(
                request.OrderId,
                DateTime.UtcNow,
                cancellationToken);

        if (result == OrderTransitionResult.Success)
        {
            var response = new AdminOrderResult(order!.ToDto(), newRowVersion!, domainEvents);
            await PublishDomainEventsAsync(domainEvents, cancellationToken);
            return response;
        }

        return result switch
        {
            OrderTransitionResult.NotFound => throw new NotFoundException(
                $"Order with id {request.OrderId} was not found.",
                "order_not_found"),
            OrderTransitionResult.InvalidTransition => throw new DomainConflictException(
                $"Cannot transition order from '{currentStatus}' to '{OrderStatus.Delivered}'.",
                "invalid_order_transition"),
            _ => throw new InvalidOperationException($"Unexpected deliver result: {result}.")
        };
    }

    private async Task PublishDomainEventsAsync(
        IReadOnlyList<object> domainEvents,
        CancellationToken cancellationToken)
    {
        foreach (var domainEvent in domainEvents)
        {
            try
            {
                await _mediator.Publish(
                    new DomainEventNotification<object>(domainEvent),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to publish domain event {DomainEventType}.",
                    domainEvent.GetType().Name);
            }
        }
    }
}

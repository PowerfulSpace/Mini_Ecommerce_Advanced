using MediatR;
using Microsoft.Extensions.Logging;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Events;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Application.Orders.DTOs;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Application.Orders.Commands;

public class CancelOrderHandler : IRequestHandler<CancelOrderCommand, CancelOrderResponse>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICurrentUser _currentUser;
    private readonly IMediator _mediator;
    private readonly ILogger<CancelOrderHandler> _logger;

    public CancelOrderHandler(
        IOrderRepository orderRepository,
        ICurrentUser currentUser,
        IMediator mediator,
        ILogger<CancelOrderHandler> logger)
    {
        _orderRepository = orderRepository;
        _currentUser = currentUser;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<CancelOrderResponse> Handle(
        CancelOrderCommand request,
        CancellationToken cancellationToken)
    {
        OrderVersionGuard.EnsurePresent(request.IfMatch);

        var userId = _currentUser.UserId;
        var items = request.Items?.Select(item => (item.OrderItemId, item.Quantity)).ToList();

        // Created -> Cancelled releases Reserved back to Available.
        // Paid -> Cancelled restores Available only; Reserved is unchanged.
        // Paid -> Cancelled also refunds the remaining payment amount (ADR-008).
        var (result, order, currentStatus, newRowVersion, domainEvents) = await _orderRepository.CancelAsync(
            request.OrderId,
            userId,
            request.IfMatch,
            items,
            cancellationToken);

        if (result == CancelOrderResult.Success)
        {
            var response = new CancelOrderResponse(order!.ToDto(), newRowVersion!, domainEvents);
            await PublishDomainEventsAsync(domainEvents, cancellationToken);
            return response;
        }

        return result switch
        {
            CancelOrderResult.NotFound => throw new NotFoundException(
                $"Order with id {request.OrderId} was not found.",
                "order_not_found"),
            CancelOrderResult.AlreadyCancelled => throw new DomainConflictException(
                "Order is already cancelled.",
                "order_already_cancelled"),
            CancelOrderResult.InvalidTransition => throw new DomainConflictException(
                $"Cannot transition order from '{currentStatus}' to '{OrderStatus.Cancelled}'.",
                "invalid_order_transition"),
            _ => throw new InvalidOperationException($"Unexpected cancel result: {result}.")
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

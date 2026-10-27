using MediatR;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Application.Orders.DTOs;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Application.Orders.Commands;

public class CancelOrderHandler : IRequestHandler<CancelOrderCommand, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICurrentUser _currentUser;

    public CancelOrderHandler(IOrderRepository orderRepository, ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _currentUser = currentUser;
    }

    public async Task<OrderDto> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;

        // Created -> Cancelled releases Reserved back to Available.
        // Paid -> Cancelled restores Available only; Reserved is unchanged.
        // Refund for Paid -> Cancelled will be connected in stage 19 (ADR-008).
        var (result, order, currentStatus) = await _orderRepository.CancelAsync(
            request.OrderId, userId, cancellationToken);

        return result switch
        {
            CancelOrderResult.Success => order!.ToDto(),
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
}

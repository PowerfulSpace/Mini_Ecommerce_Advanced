using MediatR;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Application.Orders.DTOs;

namespace PS.MiniEcommerce.Application.Orders.Queries;

public class GetOrderByIdHandler : IRequestHandler<GetOrderByIdQuery, OrderDetailsResult>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICurrentUser _currentUser;

    public GetOrderByIdHandler(IOrderRepository orderRepository, ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _currentUser = currentUser;
    }

    public async Task<OrderDetailsResult> Handle(
        GetOrderByIdQuery request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;

        var (order, rowVersion) = await _orderRepository.GetByIdWithItemsAsync(
            request.OrderId,
            userId,
            cancellationToken);

        if (order is null || rowVersion is null)
        {
            throw new NotFoundException(
                $"Order with id {request.OrderId} was not found.",
                "order_not_found");
        }

        return new OrderDetailsResult(order.ToDto(), rowVersion);
    }
}

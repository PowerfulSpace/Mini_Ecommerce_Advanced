using MediatR;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Application.Orders.DTOs;

namespace PS.MiniEcommerce.Application.Orders.Queries;

public class GetOrderByIdHandler : IRequestHandler<GetOrderByIdQuery, OrderDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICurrentUser _currentUser;

    public GetOrderByIdHandler(IOrderRepository orderRepository, ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _currentUser = currentUser;
    }

    public async Task<OrderDto> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;

        var order = await _orderRepository.GetByIdWithItemsAsync(
            request.OrderId,
            userId,
            cancellationToken);

        if (order is null)
        {
            throw new NotFoundException(
                $"Order with id {request.OrderId} was not found.",
                "order_not_found");
        }

        return order.ToDto();
    }
}

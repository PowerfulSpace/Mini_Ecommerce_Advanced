using MediatR;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Common.Pagination;
using PS.MiniEcommerce.Application.Orders.DTOs;

namespace PS.MiniEcommerce.Application.Orders.Queries;

public class GetOrdersHandler : IRequestHandler<GetOrdersQuery, PaginatedResult<OrderSummaryDto>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICurrentUser _currentUser;

    public GetOrdersHandler(IOrderRepository orderRepository, ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _currentUser = currentUser;
    }

    public async Task<PaginatedResult<OrderSummaryDto>> Handle(
        GetOrdersQuery request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;

        var (items, totalCount) = await _orderRepository.GetPagedByUserIdAsync(
            userId,
            request.Page,
            request.PageSize,
            cancellationToken);

        var dtos = items.Select(order => order.ToSummaryDto()).ToList();

        return new PaginatedResult<OrderSummaryDto>(
            dtos,
            request.Page,
            request.PageSize,
            totalCount);
    }
}

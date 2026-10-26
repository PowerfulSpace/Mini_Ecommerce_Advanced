using MediatR;
using PS.MiniEcommerce.Application.Common.Pagination;
using PS.MiniEcommerce.Application.Orders.DTOs;

namespace PS.MiniEcommerce.Application.Orders.Queries;

public sealed record GetOrdersQuery(int Page, int PageSize)
    : IRequest<PaginatedResult<OrderSummaryDto>>;

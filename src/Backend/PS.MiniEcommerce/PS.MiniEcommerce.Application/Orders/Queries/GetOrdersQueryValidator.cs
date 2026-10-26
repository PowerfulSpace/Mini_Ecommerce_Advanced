using FluentValidation;
using PS.MiniEcommerce.Application.Common.Pagination;

namespace PS.MiniEcommerce.Application.Orders.Queries;

public class GetOrdersQueryValidator : AbstractValidator<GetOrdersQuery>
{
    public GetOrdersQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).GreaterThanOrEqualTo(1).LessThanOrEqualTo(PaginationDefaults.MaxPageSize);
    }
}

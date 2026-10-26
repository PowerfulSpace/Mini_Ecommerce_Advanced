using FluentValidation;
using PS.MiniEcommerce.Application.Common.Pagination;

namespace PS.MiniEcommerce.Application.Products.Queries;

public class GetProductsQueryValidator : AbstractValidator<GetProductsQuery>
{
    public GetProductsQueryValidator()
    {
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1);
        RuleFor(query => query.PageSize).GreaterThanOrEqualTo(1).LessThanOrEqualTo(PaginationDefaults.MaxPageSize);
    }
}

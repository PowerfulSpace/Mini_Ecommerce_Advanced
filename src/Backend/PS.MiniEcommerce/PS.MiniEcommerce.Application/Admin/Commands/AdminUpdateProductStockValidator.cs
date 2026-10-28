using FluentValidation;

namespace PS.MiniEcommerce.Application.Admin.Commands;

public class AdminUpdateProductStockValidator : AbstractValidator<AdminUpdateProductStockCommand>
{
    public AdminUpdateProductStockValidator()
    {
        RuleFor(command => command.ProductId).NotEmpty();

        RuleFor(command => command.Available)
            .GreaterThanOrEqualTo(0);
    }
}

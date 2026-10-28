using FluentValidation;

namespace PS.MiniEcommerce.Application.Admin.Commands;

public class AdminReturnOrderValidator : AbstractValidator<AdminReturnOrderCommand>
{
    public AdminReturnOrderValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
    }
}

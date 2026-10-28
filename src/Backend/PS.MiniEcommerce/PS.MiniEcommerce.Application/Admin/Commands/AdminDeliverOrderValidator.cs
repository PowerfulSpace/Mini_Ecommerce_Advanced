using FluentValidation;

namespace PS.MiniEcommerce.Application.Admin.Commands;

public class AdminDeliverOrderValidator : AbstractValidator<AdminDeliverOrderCommand>
{
    public AdminDeliverOrderValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
    }
}

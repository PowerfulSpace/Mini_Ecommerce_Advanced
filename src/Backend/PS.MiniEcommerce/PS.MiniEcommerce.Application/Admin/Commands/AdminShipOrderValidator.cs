using FluentValidation;

namespace PS.MiniEcommerce.Application.Admin.Commands;

public class AdminShipOrderValidator : AbstractValidator<AdminShipOrderCommand>
{
    public AdminShipOrderValidator()
    {
        RuleFor(command => command.OrderId).NotEmpty();
    }
}

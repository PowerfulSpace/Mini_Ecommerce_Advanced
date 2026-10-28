using FluentValidation;

namespace PS.MiniEcommerce.Application.Admin.Commands;

public class AdminDeactivateProductValidator : AbstractValidator<AdminDeactivateProductCommand>
{
    public AdminDeactivateProductValidator()
    {
        RuleFor(command => command.ProductId).NotEmpty();
    }
}

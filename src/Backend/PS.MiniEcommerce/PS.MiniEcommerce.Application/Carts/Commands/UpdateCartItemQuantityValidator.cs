using FluentValidation;

namespace PS.MiniEcommerce.Application.Carts.Commands;

public class UpdateCartItemQuantityValidator : AbstractValidator<UpdateCartItemQuantityCommand>
{
    public UpdateCartItemQuantityValidator()
    {
        RuleFor(command => command.Quantity).GreaterThan(0);
    }
}

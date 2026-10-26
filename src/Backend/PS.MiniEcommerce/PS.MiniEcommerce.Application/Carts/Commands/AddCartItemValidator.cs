using FluentValidation;

namespace PS.MiniEcommerce.Application.Carts.Commands;

public class AddCartItemValidator : AbstractValidator<AddCartItemCommand>
{
    public AddCartItemValidator()
    {
        RuleFor(command => command.ProductId).NotEmpty();
        RuleFor(command => command.Quantity).GreaterThan(0);
    }
}

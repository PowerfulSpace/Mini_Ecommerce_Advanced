using FluentValidation;

namespace PS.MiniEcommerce.Application.Carts.Commands;

public class UpdateCartSelectionValidator : AbstractValidator<UpdateCartSelectionCommand>
{
    public UpdateCartSelectionValidator()
    {
        RuleFor(command => command.Items).NotEmpty();

        RuleForEach(command => command.Items)
            .ChildRules(item =>
            {
                item.RuleFor(selection => selection.ProductId).NotEmpty();
            });

        RuleFor(command => command.Items)
            .Must(items => items is null
                || items.Select(item => item.ProductId).Distinct().Count() == items.Count)
            .WithMessage("Duplicate productId values are not allowed.");
    }
}

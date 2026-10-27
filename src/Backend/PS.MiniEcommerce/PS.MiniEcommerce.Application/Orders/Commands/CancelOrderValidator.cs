using FluentValidation;

namespace PS.MiniEcommerce.Application.Orders.Commands;

public class CancelOrderValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderValidator()
    {
        RuleFor(command => command.OrderId)
            .NotEmpty();

        RuleFor(command => command.Items)
            .Must(items => items is null || items.Count > 0)
            .WithMessage("Items must not be empty when provided.");

        When(command => command.Items is not null, () =>
        {
            RuleForEach(command => command.Items)
                .ChildRules(item =>
                {
                    item.RuleFor(cancellation => cancellation.OrderItemId)
                        .NotEmpty();

                    item.RuleFor(cancellation => cancellation.Quantity)
                        .GreaterThan(0);
                });

            RuleFor(command => command.Items)
                .Must(items => items!
                    .Select(item => item.OrderItemId)
                    .Distinct()
                    .Count() == items!.Count)
                .WithMessage("Items must not contain duplicate order item ids.");
        });
    }
}

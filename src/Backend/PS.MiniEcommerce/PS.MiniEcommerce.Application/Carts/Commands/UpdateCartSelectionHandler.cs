using MediatR;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Carts.Queries;
using PS.MiniEcommerce.Application.Exceptions;

namespace PS.MiniEcommerce.Application.Carts.Commands;

public class UpdateCartSelectionHandler : IRequestHandler<UpdateCartSelectionCommand, GetCartResult>
{
    private readonly ICartRepository _cartRepository;
    private readonly IProductRepository _productRepository;
    private readonly ICurrentUser _currentUser;

    public UpdateCartSelectionHandler(
        ICartRepository cartRepository,
        IProductRepository productRepository,
        ICurrentUser currentUser)
    {
        _cartRepository = cartRepository;
        _productRepository = productRepository;
        _currentUser = currentUser;
    }

    public async Task<GetCartResult> Handle(
        UpdateCartSelectionCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        var selections = request.Items.ToDictionary(
            item => item.ProductId,
            item => item.IsSelected);

        var (cartDto, newRowVersion) = await _cartRepository.ExecuteWithLockAsync(
            userId,
            async (cart, rowVersion) =>
            {
                CartVersionGuard.Validate(request.IfMatch, rowVersion);

                foreach (var productId in selections.Keys)
                {
                    if (!cart.Items.Any(item => item.ProductId == productId))
                    {
                        throw new NotFoundException("Cart item not found.", "cart_item_not_found");
                    }
                }

                cart.SetSelection(selections, DateTime.UtcNow);

                var cartProducts = await _productRepository.GetByIdsAsync(
                    cart.Items.Select(item => item.ProductId).ToList(),
                    cancellationToken);

                return CartDtoBuilder.Build(cart, cartProducts);
            },
            cancellationToken);

        return new GetCartResult(cartDto, Convert.ToBase64String(newRowVersion));
    }
}

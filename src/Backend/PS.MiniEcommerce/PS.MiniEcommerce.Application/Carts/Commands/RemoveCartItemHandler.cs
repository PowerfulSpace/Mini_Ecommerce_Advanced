using MediatR;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Carts.Queries;
using PS.MiniEcommerce.Application.Exceptions;

namespace PS.MiniEcommerce.Application.Carts.Commands;

public class RemoveCartItemHandler : IRequestHandler<RemoveCartItemCommand, GetCartResult>
{
    private readonly ICartRepository _cartRepository;
    private readonly IProductRepository _productRepository;
    private readonly ICurrentUser _currentUser;

    public RemoveCartItemHandler(
        ICartRepository cartRepository,
        IProductRepository productRepository,
        ICurrentUser currentUser)
    {
        _cartRepository = cartRepository;
        _productRepository = productRepository;
        _currentUser = currentUser;
    }

    public async Task<GetCartResult> Handle(RemoveCartItemCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;

        var (cartDto, newRowVersion) = await _cartRepository.ExecuteWithLockAsync(
            userId,
            async (cart, rowVersion) =>
            {
                CartVersionGuard.Validate(request.IfMatch, rowVersion);

                var existingItem = cart.Items
                    .FirstOrDefault(item => item.ProductId == request.ProductId);

                if (existingItem is null)
                {
                    throw new NotFoundException("Cart item not found.", "cart_item_not_found");
                }

                cart.RemoveItem(request.ProductId, DateTime.UtcNow);

                var cartProducts = await _productRepository.GetByIdsAsync(
                    cart.Items.Select(item => item.ProductId).ToList(),
                    cancellationToken);

                return CartDtoBuilder.Build(cart, cartProducts);
            },
            cancellationToken);

        return new GetCartResult(cartDto, Convert.ToBase64String(newRowVersion));
    }
}

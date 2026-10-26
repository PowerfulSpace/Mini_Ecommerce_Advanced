using MediatR;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Carts;

namespace PS.MiniEcommerce.Application.Carts.Queries;

public class GetCartHandler : IRequestHandler<GetCartQuery, GetCartResult>
{
    private readonly ICartRepository _cartRepository;
    private readonly IProductRepository _productRepository;
    private readonly ICurrentUser _currentUser;

    public GetCartHandler(
        ICartRepository cartRepository,
        IProductRepository productRepository,
        ICurrentUser currentUser)
    {
        _cartRepository = cartRepository;
        _productRepository = productRepository;
        _currentUser = currentUser;
    }

    public async Task<GetCartResult> Handle(GetCartQuery request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;

        var cart = await _cartRepository.GetByUserIdAsync(userId, cancellationToken);

        if (cart is null)
        {
            cart = await _cartRepository.CreateAsync(userId, DateTime.UtcNow, cancellationToken);
        }

        var rowVersion = await _cartRepository.GetRowVersionAsync(cart.Id, cancellationToken);
        var version = Convert.ToBase64String(rowVersion);

        var productIds = cart.Items.Select(item => item.ProductId).ToList();
        var products = await _productRepository.GetByIdsAsync(productIds, cancellationToken);

        var cartDto = CartDtoBuilder.Build(cart, products);

        return new GetCartResult(cartDto, version);
    }
}

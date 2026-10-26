using MediatR;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Carts.Queries;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Application.Carts.Commands;

public class AddCartItemHandler : IRequestHandler<AddCartItemCommand, GetCartResult>
{
    private readonly ICartRepository _cartRepository;
    private readonly IProductRepository _productRepository;
    private readonly ICurrentUser _currentUser;

    public AddCartItemHandler(
        ICartRepository cartRepository,
        IProductRepository productRepository,
        ICurrentUser currentUser)
    {
        _cartRepository = cartRepository;
        _productRepository = productRepository;
        _currentUser = currentUser;
    }

    public async Task<GetCartResult> Handle(AddCartItemCommand request, CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;

        var (cartDto, newRowVersion) = await _cartRepository.ExecuteWithLockAsync(
            userId,
            async (cart, rowVersion) =>
            {
                CartVersionGuard.Validate(request.IfMatch, rowVersion);

                var products = await _productRepository.GetByIdsAsync(
                    new[] { request.ProductId },
                    cancellationToken);

                var product = products.SingleOrDefault();
                if (product is null)
                {
                    throw new NotFoundException(
                        $"Product with id {request.ProductId} was not found.",
                        "product_not_found");
                }

                if (!product.IsActive)
                {
                    throw new DomainConflictException(
                        $"Product '{product.Name}' is not available.",
                        "product_unavailable");
                }

                var existingQuantity = cart.Items
                    .FirstOrDefault(item => item.ProductId == request.ProductId)?.Quantity ?? 0;

                if (existingQuantity + request.Quantity > product.StockQuantity)
                {
                    throw new DomainConflictException(
                        $"Insufficient stock for product '{product.Name}'.",
                        "insufficient_stock");
                }

                cart.AddItem(request.ProductId, request.Quantity, DateTime.UtcNow);

                var cartProducts = await _productRepository.GetByIdsAsync(
                    cart.Items.Select(item => item.ProductId).ToList(),
                    cancellationToken);

                return CartDtoBuilder.Build(cart, cartProducts);
            },
            cancellationToken);

        return new GetCartResult(cartDto, Convert.ToBase64String(newRowVersion));
    }
}

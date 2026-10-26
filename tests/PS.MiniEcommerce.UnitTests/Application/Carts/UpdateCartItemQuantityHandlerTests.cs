using NSubstitute;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Carts.Commands;
using PS.MiniEcommerce.Application.Carts.DTOs;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.UnitTests.Application.Carts;

public class UpdateCartItemQuantityHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] RowVersion = { 1, 2, 3, 4, 5, 6, 7, 8 };
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static string ValidIfMatch => $"\"{Convert.ToBase64String(RowVersion)}\"";

    [Fact]
    public async Task Handle_WhenItemExists_UpdatesQuantity()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);
        var product = Product.Create("Wireless Mouse", "Description.", 10m, 10, FixedUtcNow);
        cart.AddItem(product.Id, 2, FixedUtcNow);
        var handler = CreateHandler(cart, product);

        var result = await handler.Handle(
            new UpdateCartItemQuantityCommand(product.Id, 5, ValidIfMatch),
            CancellationToken.None);

        var item = Assert.Single(result.Cart.Items);
        Assert.Equal(product.Id, item.ProductId);
        Assert.Equal(5, item.Quantity);
        Assert.Equal(50m, result.Cart.TotalAmount);
    }

    [Fact]
    public async Task Handle_WhenItemDoesNotExist_ThrowsNotFoundWithCartItemNotFound()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);
        var product = Product.Create("Wireless Mouse", "Description.", 10m, 10, FixedUtcNow);
        var handler = CreateHandler(cart, product);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(
                new UpdateCartItemQuantityCommand(product.Id, 5, ValidIfMatch),
                CancellationToken.None));

        Assert.Equal("cart_item_not_found", exception.Code);
    }

    [Fact]
    public async Task Handle_WhenProductInactive_ThrowsDomainConflictWithProductUnavailable()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);
        var product = Product.Create("Discontinued Tablet Case", "Description.", 10m, 10, FixedUtcNow);
        cart.AddItem(product.Id, 2, FixedUtcNow);
        product.Deactivate();
        var handler = CreateHandler(cart, product);

        var exception = await Assert.ThrowsAsync<DomainConflictException>(
            () => handler.Handle(
                new UpdateCartItemQuantityCommand(product.Id, 5, ValidIfMatch),
                CancellationToken.None));

        Assert.Equal("product_unavailable", exception.Code);
    }

    [Fact]
    public async Task Handle_WhenQuantityExceedsStock_ThrowsDomainConflictWithInsufficientStock()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);
        var product = Product.Create("Limited Edition Mug", "Description.", 10m, 3, FixedUtcNow);
        cart.AddItem(product.Id, 1, FixedUtcNow);
        var handler = CreateHandler(cart, product);

        var exception = await Assert.ThrowsAsync<DomainConflictException>(
            () => handler.Handle(
                new UpdateCartItemQuantityCommand(product.Id, 5, ValidIfMatch),
                CancellationToken.None));

        Assert.Equal("insufficient_stock", exception.Code);
    }

    private static UpdateCartItemQuantityHandler CreateHandler(Cart cart, params Product[] products)
    {
        var cartRepository = Substitute.For<ICartRepository>();
        cartRepository
            .ExecuteWithLockAsync(
                Arg.Any<Guid>(),
                Arg.Any<Func<Cart, byte[], Task<CartDto>>>(),
                Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                var action = callInfo.Arg<Func<Cart, byte[], Task<CartDto>>>();
                var result = await action(cart, RowVersion);
                return (result, RowVersion);
            });

        var productRepository = Substitute.For<IProductRepository>();
        productRepository
            .GetByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Product>)products.ToList());

        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(UserId);

        return new UpdateCartItemQuantityHandler(cartRepository, productRepository, currentUser);
    }
}

using NSubstitute;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Carts.Commands;
using PS.MiniEcommerce.Application.Carts.DTOs;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.UnitTests.Application.Carts;

public class UpdateCartSelectionHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] RowVersion = { 1, 2, 3, 4, 5, 6, 7, 8 };
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static string ValidIfMatch => $"\"{Convert.ToBase64String(RowVersion)}\"";

    [Fact]
    public async Task Handle_WhenSelectionChanges_UpdatesSelectionAndTotals()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);
        var first = Product.Create("Wireless Mouse", "Description.", Money.FromDecimal(10m), 10, FixedUtcNow);
        var second = Product.Create("Ergonomic Keyboard", "Description.", Money.FromDecimal(20m), 10, FixedUtcNow);
        cart.AddItem(first.Id, 1, FixedUtcNow);
        cart.AddItem(second.Id, 2, FixedUtcNow);
        var handler = CreateHandler(cart, first, second);

        var result = await handler.Handle(
            new UpdateCartSelectionCommand(
                new[]
                {
                    new CartItemSelectionDto(first.Id, false),
                    new CartItemSelectionDto(second.Id, true)
                },
                ValidIfMatch),
            CancellationToken.None);

        Assert.False(result.Cart.Items.Single(item => item.ProductId == first.Id).IsSelected);
        Assert.True(result.Cart.Items.Single(item => item.ProductId == second.Id).IsSelected);
        Assert.Equal(50m, result.Cart.TotalAmount);
        Assert.Equal(40m, result.Cart.SelectedTotalAmount);
        Assert.NotEqual(FixedUtcNow, cart.UpdatedAtUtc);
        Assert.Equal(Convert.ToBase64String(RowVersion), result.Version);
    }

    [Fact]
    public async Task Handle_WhenProductIsNotInCart_ThrowsNotFoundWithCartItemNotFound()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);
        var product = Product.Create("Wireless Mouse", "Description.", Money.FromDecimal(10m), 10, FixedUtcNow);
        cart.AddItem(product.Id, 1, FixedUtcNow);
        var handler = CreateHandler(cart, product);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(
                new UpdateCartSelectionCommand(
                    new[] { new CartItemSelectionDto(Guid.NewGuid(), false) },
                    ValidIfMatch),
                CancellationToken.None));

        Assert.Equal("cart_item_not_found", exception.Code);
    }

    [Fact]
    public async Task Handle_WithStaleIfMatch_ThrowsCartVersionConflict()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);
        var product = Product.Create("Wireless Mouse", "Description.", Money.FromDecimal(10m), 10, FixedUtcNow);
        cart.AddItem(product.Id, 1, FixedUtcNow);
        var handler = CreateHandler(cart, product);

        var exception = await Assert.ThrowsAsync<DomainConflictException>(
            () => handler.Handle(
                new UpdateCartSelectionCommand(
                    new[] { new CartItemSelectionDto(product.Id, false) },
                    "\"AAAAAAAAAAAAAAAAAAAAAA==\""),
                CancellationToken.None));

        Assert.Equal("cart_version_conflict", exception.Code);
    }

    [Fact]
    public async Task Handle_WithoutIfMatch_ThrowsPreconditionRequired()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);
        var product = Product.Create("Wireless Mouse", "Description.", Money.FromDecimal(10m), 10, FixedUtcNow);
        cart.AddItem(product.Id, 1, FixedUtcNow);
        var handler = CreateHandler(cart, product);

        var exception = await Assert.ThrowsAsync<DomainConflictException>(
            () => handler.Handle(
                new UpdateCartSelectionCommand(
                    new[] { new CartItemSelectionDto(product.Id, false) },
                    null),
                CancellationToken.None));

        Assert.Equal("precondition_required", exception.Code);
    }

    private static UpdateCartSelectionHandler CreateHandler(Cart cart, params Product[] products)
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

        return new UpdateCartSelectionHandler(cartRepository, productRepository, currentUser);
    }
}

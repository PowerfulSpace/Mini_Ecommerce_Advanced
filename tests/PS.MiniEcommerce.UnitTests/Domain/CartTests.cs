using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.UnitTests.Domain;

public class CartTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LaterUtcNow = new(2026, 1, 15, 12, 5, 0, DateTimeKind.Utc);

    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProductA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ProductB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid ProductC = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public void Create_WithValidUserId_SetsProperties()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);

        Assert.NotEqual(Guid.Empty, cart.Id);
        Assert.Equal(UserId, cart.UserId);
        Assert.Equal(FixedUtcNow, cart.CreatedAtUtc);
        Assert.Equal(FixedUtcNow, cart.UpdatedAtUtc);
        Assert.Empty(cart.Items);
    }

    [Fact]
    public void AddItem_WithNewProduct_AddsItemAndUpdatesTimestamp()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);

        cart.AddItem(ProductA, 3, LaterUtcNow);

        var item = Assert.Single(cart.Items);
        Assert.Equal(ProductA, item.ProductId);
        Assert.Equal(3, item.Quantity);
        Assert.Equal(LaterUtcNow, cart.UpdatedAtUtc);
    }

    [Fact]
    public void AddItem_WithExistingProduct_IncreasesQuantity()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);
        cart.AddItem(ProductA, 2, FixedUtcNow);

        cart.AddItem(ProductA, 3, LaterUtcNow);

        var item = Assert.Single(cart.Items);
        Assert.Equal(5, item.Quantity);
        Assert.Equal(LaterUtcNow, cart.UpdatedAtUtc);
    }

    [Fact]
    public void AddItem_WithZeroQuantity_ThrowsDomainValidationException()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);

        Assert.Throws<DomainValidationException>(() => cart.AddItem(ProductA, 0, FixedUtcNow));
    }

    [Fact]
    public void AddItem_WithNegativeQuantity_ThrowsDomainValidationException()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);

        Assert.Throws<DomainValidationException>(() => cart.AddItem(ProductA, -1, FixedUtcNow));
    }

    [Fact]
    public void AddItem_WithMultipleProducts_ContainsAll()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);

        cart.AddItem(ProductA, 1, FixedUtcNow);
        cart.AddItem(ProductB, 2, FixedUtcNow);
        cart.AddItem(ProductC, 3, FixedUtcNow);

        Assert.Equal(3, cart.Items.Count);
        Assert.Contains(cart.Items, item => item.ProductId == ProductA && item.Quantity == 1);
        Assert.Contains(cart.Items, item => item.ProductId == ProductB && item.Quantity == 2);
        Assert.Contains(cart.Items, item => item.ProductId == ProductC && item.Quantity == 3);
    }

    [Fact]
    public void AddItem_WithExistingProduct_DoesNotChangeCreatedAtUtc()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);
        cart.AddItem(ProductA, 2, FixedUtcNow);

        cart.AddItem(ProductA, 3, LaterUtcNow);

        var item = Assert.Single(cart.Items);
        Assert.Equal(FixedUtcNow, item.CreatedAtUtc);
        Assert.Equal(LaterUtcNow, item.UpdatedAtUtc);
    }

    [Fact]
    public void UpdateItemQuantity_WithExistingItem_ReplacesQuantityAndUpdatesTimestamp()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);
        cart.AddItem(ProductA, 2, FixedUtcNow);

        cart.UpdateItemQuantity(ProductA, 7, LaterUtcNow);

        var item = Assert.Single(cart.Items);
        Assert.Equal(7, item.Quantity);
        Assert.Equal(LaterUtcNow, item.UpdatedAtUtc);
        Assert.Equal(LaterUtcNow, cart.UpdatedAtUtc);
    }

    [Fact]
    public void UpdateItemQuantity_WithMissingProduct_ThrowsDomainConflictException()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);

        Assert.Throws<DomainConflictException>(() => cart.UpdateItemQuantity(ProductA, 1, FixedUtcNow));
    }

    [Fact]
    public void UpdateItemQuantity_WithZeroQuantity_ThrowsDomainValidationException()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);
        cart.AddItem(ProductA, 2, FixedUtcNow);

        Assert.Throws<DomainValidationException>(() => cart.UpdateItemQuantity(ProductA, 0, FixedUtcNow));
    }

    [Fact]
    public void UpdateItemQuantity_WithNegativeQuantity_ThrowsDomainValidationException()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);
        cart.AddItem(ProductA, 2, FixedUtcNow);

        Assert.Throws<DomainValidationException>(() => cart.UpdateItemQuantity(ProductA, -1, FixedUtcNow));
    }

    [Fact]
    public void RemoveItem_WithExistingItem_RemovesIt()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);
        cart.AddItem(ProductA, 2, FixedUtcNow);

        cart.RemoveItem(ProductA, LaterUtcNow);

        Assert.Empty(cart.Items);
        Assert.Equal(LaterUtcNow, cart.UpdatedAtUtc);
    }

    [Fact]
    public void RemoveItem_WithMissingProduct_ThrowsDomainConflictException()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);

        Assert.Throws<DomainConflictException>(() => cart.RemoveItem(ProductA, FixedUtcNow));
    }

    [Fact]
    public void RemoveItem_WithMultipleItems_LeavesOthersUntouched()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);
        cart.AddItem(ProductA, 1, FixedUtcNow);
        cart.AddItem(ProductB, 2, FixedUtcNow);
        cart.AddItem(ProductC, 3, FixedUtcNow);

        cart.RemoveItem(ProductB, LaterUtcNow);

        Assert.Equal(2, cart.Items.Count);
        Assert.Contains(cart.Items, item => item.ProductId == ProductA && item.Quantity == 1);
        Assert.Contains(cart.Items, item => item.ProductId == ProductC && item.Quantity == 3);
        Assert.DoesNotContain(cart.Items, item => item.ProductId == ProductB);
    }

    [Fact]
    public void Clear_WithItems_EmptiesItemsAndUpdatesTimestamp()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);
        cart.AddItem(ProductA, 1, FixedUtcNow);
        cart.AddItem(ProductB, 2, FixedUtcNow);

        cart.Clear(LaterUtcNow);

        Assert.Empty(cart.Items);
        Assert.Equal(LaterUtcNow, cart.UpdatedAtUtc);
    }

    [Fact]
    public void Clear_OnEmptyCart_DoesNotThrowAndUpdatesTimestamp()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);

        cart.Clear(LaterUtcNow);

        Assert.Empty(cart.Items);
        Assert.Equal(LaterUtcNow, cart.UpdatedAtUtc);
    }

    [Fact]
    public void AddItem_WithNewProduct_SetsCartIdOnItem()
    {
        var cart = Cart.Create(UserId, FixedUtcNow);

        cart.AddItem(ProductA, 1, FixedUtcNow);

        var item = Assert.Single(cart.Items);
        Assert.Equal(cart.Id, item.CartId);
    }
}

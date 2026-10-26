using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.UnitTests.Domain;

public class OrderTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProductA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ProductB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void Create_WithSingleItem_SetsProperties()
    {
        var data = new OrderItemData(ProductA, "Widget", 19.99m, 3);

        var order = Order.Create(UserId, new[] { data }, FixedUtcNow);

        Assert.NotEqual(Guid.Empty, order.Id);
        Assert.Equal(UserId, order.UserId);
        Assert.Equal(OrderStatus.Created, order.Status);
        Assert.Equal(FixedUtcNow, order.CreatedAtUtc);
        Assert.Equal(59.97m, order.TotalAmount);
        Assert.Single(order.Items);
    }

    [Fact]
    public void Create_WithMultipleItems_CalculatesTotalAmount()
    {
        var items = new[]
        {
            new OrderItemData(ProductA, "Widget", 10.00m, 2),
            new OrderItemData(ProductB, "Gadget", 5.50m, 4)
        };

        var order = Order.Create(UserId, items, FixedUtcNow);

        Assert.Equal(2, order.Items.Count);
        Assert.Equal(42.00m, order.TotalAmount);
    }

    [Fact]
    public void Create_WithNullItems_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(() => Order.Create(UserId, null!, FixedUtcNow));
    }

    [Fact]
    public void Create_WithEmptyItems_ThrowsDomainValidationException()
    {
        Assert.Throws<DomainValidationException>(() => Order.Create(UserId, Array.Empty<OrderItemData>(), FixedUtcNow));
    }

    [Fact]
    public void Create_WithSingleItem_SetsOrderIdOnItem()
    {
        var data = new OrderItemData(ProductA, "Widget", 19.99m, 1);

        var order = Order.Create(UserId, new[] { data }, FixedUtcNow);

        var item = Assert.Single(order.Items);
        Assert.Equal(order.Id, item.OrderId);
    }

    [Fact]
    public void Create_WithSingleItem_CopiesSnapshotValues()
    {
        var data = new OrderItemData(ProductA, "Widget", 19.99m, 3);

        var order = Order.Create(UserId, new[] { data }, FixedUtcNow);

        var item = Assert.Single(order.Items);
        Assert.Equal(ProductA, item.ProductId);
        Assert.Equal("Widget", item.ProductName);
        Assert.Equal(19.99m, item.UnitPrice);
        Assert.Equal(3, item.Quantity);
    }

    [Fact]
    public void Create_WithNullProductName_ThrowsDomainValidationException()
    {
        var data = new OrderItemData(ProductA, null!, 10m, 1);

        Assert.Throws<DomainValidationException>(() => Order.Create(UserId, new[] { data }, FixedUtcNow));
    }

    [Fact]
    public void Create_WithEmptyProductName_ThrowsDomainValidationException()
    {
        var data = new OrderItemData(ProductA, string.Empty, 10m, 1);

        Assert.Throws<DomainValidationException>(() => Order.Create(UserId, new[] { data }, FixedUtcNow));
    }

    [Fact]
    public void Create_WithWhitespaceProductName_ThrowsDomainValidationException()
    {
        var data = new OrderItemData(ProductA, "   ", 10m, 1);

        Assert.Throws<DomainValidationException>(() => Order.Create(UserId, new[] { data }, FixedUtcNow));
    }

    [Fact]
    public void Create_WithProductNameLongerThan200Characters_ThrowsDomainValidationException()
    {
        var data = new OrderItemData(ProductA, new string('a', 201), 10m, 1);

        Assert.Throws<DomainValidationException>(() => Order.Create(UserId, new[] { data }, FixedUtcNow));
    }

    [Fact]
    public void Create_WithNegativeUnitPrice_ThrowsDomainValidationException()
    {
        var data = new OrderItemData(ProductA, "Widget", -0.01m, 1);

        Assert.Throws<DomainValidationException>(() => Order.Create(UserId, new[] { data }, FixedUtcNow));
    }

    [Fact]
    public void Create_WithZeroQuantity_ThrowsDomainValidationException()
    {
        var data = new OrderItemData(ProductA, "Widget", 10m, 0);

        Assert.Throws<DomainValidationException>(() => Order.Create(UserId, new[] { data }, FixedUtcNow));
    }

    [Fact]
    public void Create_WithNegativeQuantity_ThrowsDomainValidationException()
    {
        var data = new OrderItemData(ProductA, "Widget", 10m, -1);

        Assert.Throws<DomainValidationException>(() => Order.Create(UserId, new[] { data }, FixedUtcNow));
    }

    [Fact]
    public void Cancel_OnCreatedOrder_SetsStatusCancelled()
    {
        var order = Order.Create(UserId, new[] { new OrderItemData(ProductA, "Widget", 10m, 1) }, FixedUtcNow);

        order.Cancel();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_ThrowsDomainConflictException()
    {
        var order = Order.Create(UserId, new[] { new OrderItemData(ProductA, "Widget", 10m, 1) }, FixedUtcNow);
        order.Cancel();

        Assert.Throws<DomainConflictException>(() => order.Cancel());
    }

    [Fact]
    public void Create_WithZeroUnitPrices_TotalAmountIsZero()
    {
        var items = new[]
        {
            new OrderItemData(ProductA, "Free widget", 0m, 5),
            new OrderItemData(ProductB, "Free gadget", 0m, 2)
        };

        var order = Order.Create(UserId, items, FixedUtcNow);

        Assert.Equal(0m, order.TotalAmount);
    }

    [Fact]
    public void Create_WithDecimalPrices_PreservesTotalPrecision()
    {
        var data = new OrderItemData(ProductA, "Widget", 19.99m, 3);

        var order = Order.Create(UserId, new[] { data }, FixedUtcNow);

        Assert.Equal(59.97m, order.TotalAmount);
    }
}

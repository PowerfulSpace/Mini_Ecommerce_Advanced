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
        var data = new OrderItemData(ProductA, "Widget", Money.FromDecimal(19.99m), 3);

        var order = Order.Create(UserId, new[] { data }, FixedUtcNow);

        Assert.NotEqual(Guid.Empty, order.Id);
        Assert.Equal(UserId, order.UserId);
        Assert.Equal(OrderStatus.Created, order.Status);
        Assert.Equal(FixedUtcNow, order.CreatedAtUtc);
        Assert.Equal(59.97m, order.TotalAmount.Amount);
        Assert.Single(order.Items);
    }

    [Fact]
    public void Create_WithMultipleItems_CalculatesTotalAmount()
    {
        var items = new[]
        {
            new OrderItemData(ProductA, "Widget", Money.FromDecimal(10.00m), 2),
            new OrderItemData(ProductB, "Gadget", Money.FromDecimal(5.50m), 4)
        };

        var order = Order.Create(UserId, items, FixedUtcNow);

        Assert.Equal(2, order.Items.Count);
        Assert.Equal(42.00m, order.TotalAmount.Amount);
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
        var data = new OrderItemData(ProductA, "Widget", Money.FromDecimal(19.99m), 1);

        var order = Order.Create(UserId, new[] { data }, FixedUtcNow);

        var item = Assert.Single(order.Items);
        Assert.Equal(order.Id, item.OrderId);
    }

    [Fact]
    public void Create_WithSingleItem_CopiesSnapshotValues()
    {
        var data = new OrderItemData(ProductA, "Widget", Money.FromDecimal(19.99m), 3);

        var order = Order.Create(UserId, new[] { data }, FixedUtcNow);

        var item = Assert.Single(order.Items);
        Assert.Equal(ProductA, item.ProductId);
        Assert.Equal("Widget", item.ProductName);
        Assert.Equal(19.99m, item.UnitPrice.Amount);
        Assert.Equal(3, item.Quantity);
    }

    [Fact]
    public void Create_WithNullProductName_ThrowsDomainValidationException()
    {
        var data = new OrderItemData(ProductA, null!, Money.FromDecimal(10m), 1);

        Assert.Throws<DomainValidationException>(() => Order.Create(UserId, new[] { data }, FixedUtcNow));
    }

    [Fact]
    public void Create_WithEmptyProductName_ThrowsDomainValidationException()
    {
        var data = new OrderItemData(ProductA, string.Empty, Money.FromDecimal(10m), 1);

        Assert.Throws<DomainValidationException>(() => Order.Create(UserId, new[] { data }, FixedUtcNow));
    }

    [Fact]
    public void Create_WithWhitespaceProductName_ThrowsDomainValidationException()
    {
        var data = new OrderItemData(ProductA, "   ", Money.FromDecimal(10m), 1);

        Assert.Throws<DomainValidationException>(() => Order.Create(UserId, new[] { data }, FixedUtcNow));
    }

    [Fact]
    public void Create_WithProductNameLongerThan200Characters_ThrowsDomainValidationException()
    {
        var data = new OrderItemData(ProductA, new string('a', 201), Money.FromDecimal(10m), 1);

        Assert.Throws<DomainValidationException>(() => Order.Create(UserId, new[] { data }, FixedUtcNow));
    }

    [Fact]
    public void Create_WithNegativeUnitPrice_ThrowsDomainValidationException()
    {
        var data = new OrderItemData(ProductA, "Widget", Money.FromDecimal(-0.01m), 1);

        Assert.Throws<DomainValidationException>(() => Order.Create(UserId, new[] { data }, FixedUtcNow));
    }

    [Fact]
    public void Create_WithZeroQuantity_ThrowsDomainValidationException()
    {
        var data = new OrderItemData(ProductA, "Widget", Money.FromDecimal(10m), 0);

        Assert.Throws<DomainValidationException>(() => Order.Create(UserId, new[] { data }, FixedUtcNow));
    }

    [Fact]
    public void Create_WithNegativeQuantity_ThrowsDomainValidationException()
    {
        var data = new OrderItemData(ProductA, "Widget", Money.FromDecimal(10m), -1);

        Assert.Throws<DomainValidationException>(() => Order.Create(UserId, new[] { data }, FixedUtcNow));
    }

    [Fact]
    public void MarkPaid_WithPaymentId_SetsStatusPaid()
    {
        var order = CreateOrder(Guid.NewGuid());

        order.MarkPaid();

        Assert.Equal(OrderStatus.Paid, order.Status);
    }

    [Fact]
    public void MarkPaid_WithoutPaymentId_ThrowsInvalidOrderTransition()
    {
        var order = CreateOrder();

        var exception = AssertInvalidTransition(order.MarkPaid);

        Assert.Contains("Created", exception.Message);
        Assert.Contains("Paid", exception.Message);
        Assert.Contains("payment is required", exception.Message);
    }

    [Fact]
    public void Ship_OnPaidOrder_SetsStatusShipped()
    {
        var order = CreatePaidOrder();

        order.Ship();

        Assert.Equal(OrderStatus.Shipped, order.Status);
    }

    [Fact]
    public void Deliver_OnShippedOrder_SetsStatusDelivered()
    {
        var order = CreateShippedOrder();

        order.Deliver();

        Assert.Equal(OrderStatus.Delivered, order.Status);
    }

    [Fact]
    public void Cancel_OnCreatedOrder_SetsStatusCancelled()
    {
        var order = CreateOrder();

        order.Cancel();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Cancel_OnPaidOrder_SetsStatusCancelled()
    {
        var order = CreatePaidOrder();

        order.Cancel();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Fact]
    public void Return_OnDeliveredOrder_SetsStatusReturned()
    {
        var order = CreateDeliveredOrder();

        order.Return();

        Assert.Equal(OrderStatus.Returned, order.Status);
    }

    [Fact]
    public void Ship_OnCreatedOrder_ThrowsInvalidOrderTransition()
    {
        var order = CreateOrder();

        var exception = AssertInvalidTransition(order.Ship);

        Assert.Contains("Created", exception.Message);
        Assert.Contains("Shipped", exception.Message);
    }

    [Fact]
    public void Deliver_OnCreatedOrder_ThrowsInvalidOrderTransition()
    {
        var order = CreateOrder();

        var exception = AssertInvalidTransition(order.Deliver);

        Assert.Contains("Created", exception.Message);
        Assert.Contains("Delivered", exception.Message);
    }

    [Fact]
    public void Deliver_OnPaidOrder_ThrowsInvalidOrderTransition()
    {
        var order = CreatePaidOrder();

        var exception = AssertInvalidTransition(order.Deliver);

        Assert.Contains("Paid", exception.Message);
        Assert.Contains("Delivered", exception.Message);
    }

    [Fact]
    public void Cancel_OnShippedOrder_ThrowsInvalidOrderTransition()
    {
        var order = CreateShippedOrder();

        var exception = AssertInvalidTransition(order.Cancel);

        Assert.Contains("Shipped", exception.Message);
        Assert.Contains("Cancelled", exception.Message);
    }

    [Fact]
    public void Cancel_OnDeliveredOrder_ThrowsInvalidOrderTransition()
    {
        var order = CreateDeliveredOrder();

        var exception = AssertInvalidTransition(order.Cancel);

        Assert.Contains("Delivered", exception.Message);
        Assert.Contains("Cancelled", exception.Message);
    }

    [Fact]
    public void MarkPaid_WhenAlreadyPaid_ThrowsInvalidOrderTransition()
    {
        var order = CreatePaidOrder();

        AssertInvalidTransition(order.MarkPaid);
    }

    [Fact]
    public void Ship_WhenAlreadyShipped_ThrowsInvalidOrderTransition()
    {
        var order = CreateShippedOrder();

        AssertInvalidTransition(order.Ship);
    }

    [Fact]
    public void Deliver_WhenAlreadyDelivered_ThrowsInvalidOrderTransition()
    {
        var order = CreateDeliveredOrder();

        AssertInvalidTransition(order.Deliver);
    }

    [Fact]
    public void Cancel_WhenAlreadyCancelled_ThrowsInvalidOrderTransition()
    {
        var order = CreateCancelledOrder();

        var exception = AssertInvalidTransition(order.Cancel);

        Assert.Contains("Cancelled", exception.Message);
    }

    [Fact]
    public void Return_WhenAlreadyReturned_ThrowsInvalidOrderTransition()
    {
        var order = CreateReturnedOrder();

        var exception = AssertInvalidTransition(order.Return);

        Assert.Contains("Returned", exception.Message);
    }

    [Theory]
    [MemberData(nameof(TerminalStatusTransitions))]
    public void Transition_FromTerminalStatus_ThrowsInvalidOrderTransition(
        OrderStatus status,
        Action<Order> transition)
    {
        var order = CreateOrderInStatus(status);

        var exception = AssertInvalidTransition(() => transition(order));

        Assert.Contains(status.ToString(), exception.Message);
    }

    public static IEnumerable<object[]> TerminalStatusTransitions()
    {
        yield return new object[] { OrderStatus.Cancelled, (Action<Order>)(order => order.MarkPaid()) };
        yield return new object[] { OrderStatus.Cancelled, (Action<Order>)(order => order.Ship()) };
        yield return new object[] { OrderStatus.Cancelled, (Action<Order>)(order => order.Deliver()) };
        yield return new object[] { OrderStatus.Cancelled, (Action<Order>)(order => order.Cancel()) };
        yield return new object[] { OrderStatus.Cancelled, (Action<Order>)(order => order.Return()) };
        yield return new object[] { OrderStatus.Returned, (Action<Order>)(order => order.MarkPaid()) };
        yield return new object[] { OrderStatus.Returned, (Action<Order>)(order => order.Ship()) };
        yield return new object[] { OrderStatus.Returned, (Action<Order>)(order => order.Deliver()) };
        yield return new object[] { OrderStatus.Returned, (Action<Order>)(order => order.Cancel()) };
        yield return new object[] { OrderStatus.Returned, (Action<Order>)(order => order.Return()) };
    }

    [Fact]
    public void Create_WithZeroUnitPrices_TotalAmountIsZero()
    {
        var items = new[]
        {
            new OrderItemData(ProductA, "Free widget", Money.FromDecimal(0m), 5),
            new OrderItemData(ProductB, "Free gadget", Money.FromDecimal(0m), 2)
        };

        var order = Order.Create(UserId, items, FixedUtcNow);

        Assert.Equal(0m, order.TotalAmount.Amount);
    }

    [Fact]
    public void Create_WithDecimalPrices_PreservesTotalPrecision()
    {
        var data = new OrderItemData(ProductA, "Widget", Money.FromDecimal(19.99m), 3);

        var order = Order.Create(UserId, new[] { data }, FixedUtcNow);

        Assert.Equal(59.97m, order.TotalAmount.Amount);
    }

    private static Order CreateOrder(Guid? paymentId = null) =>
        Order.Create(
            UserId,
            new[] { new OrderItemData(ProductA, "Widget", Money.FromDecimal(10m), 1) },
            FixedUtcNow,
            paymentId);

    private static Order CreatePaidOrder()
    {
        var order = CreateOrder(Guid.NewGuid());
        order.MarkPaid();
        return order;
    }

    private static Order CreateShippedOrder()
    {
        var order = CreatePaidOrder();
        order.Ship();
        return order;
    }

    private static Order CreateDeliveredOrder()
    {
        var order = CreateShippedOrder();
        order.Deliver();
        return order;
    }

    private static Order CreateCancelledOrder()
    {
        var order = CreateOrder();
        order.Cancel();
        return order;
    }

    private static Order CreateReturnedOrder()
    {
        var order = CreateDeliveredOrder();
        order.Return();
        return order;
    }

    private static Order CreateOrderInStatus(OrderStatus status) => status switch
    {
        OrderStatus.Created => CreateOrder(),
        OrderStatus.Paid => CreatePaidOrder(),
        OrderStatus.Shipped => CreateShippedOrder(),
        OrderStatus.Delivered => CreateDeliveredOrder(),
        OrderStatus.Cancelled => CreateCancelledOrder(),
        OrderStatus.Returned => CreateReturnedOrder(),
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    private static DomainConflictException AssertInvalidTransition(Action action)
    {
        var exception = Assert.Throws<DomainConflictException>(action);
        Assert.Equal("invalid_order_transition", exception.Code);
        return exception;
    }
}

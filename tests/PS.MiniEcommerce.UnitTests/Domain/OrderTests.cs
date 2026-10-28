using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Domain.Exceptions;
using PS.MiniEcommerce.Domain.Payments;

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
    public void SetPaymentId_WhenPaymentIdIsNull_SetsPaymentId()
    {
        var order = CreateOrder();
        var paymentId = Guid.NewGuid();

        order.SetPaymentId(paymentId);

        Assert.Equal(paymentId, order.PaymentId);
        Assert.Null(order.Payment);
    }

    [Fact]
    public void SetPaymentId_WhenAlreadySet_ThrowsDomainConflictException()
    {
        var order = CreateOrder();
        order.SetPaymentId(Guid.NewGuid());

        var exception = Assert.Throws<DomainConflictException>(
            () => order.SetPaymentId(Guid.NewGuid()));

        Assert.Equal("invalid_order_transition", exception.Code);
    }

    [Fact]
    public void SetPayment_SetsPaymentIdAndNavigation()
    {
        var order = CreateOrder();
        var payment = Payment.Create(order.Id, order.TotalAmount, FixedUtcNow);

        order.SetPayment(payment);

        Assert.Equal(payment.Id, order.PaymentId);
        Assert.Same(payment, order.Payment);
    }

    [Fact]
    public void SetPayment_AfterSetPaymentIdWithSamePayment_SetsNavigation()
    {
        var order = CreateOrder();
        var payment = Payment.Create(order.Id, order.TotalAmount, FixedUtcNow);
        order.SetPaymentId(payment.Id);

        order.SetPayment(payment);

        Assert.Equal(payment.Id, order.PaymentId);
        Assert.Same(payment, order.Payment);
    }

    [Fact]
    public void SetPayment_WithPaymentForAnotherOrder_ThrowsDomainValidationException()
    {
        var order = CreateOrder();
        var payment = Payment.Create(Guid.NewGuid(), order.TotalAmount, FixedUtcNow);

        Assert.Throws<DomainValidationException>(() => order.SetPayment(payment));
        Assert.Null(order.PaymentId);
        Assert.Null(order.Payment);
    }

    [Fact]
    public void SetPayment_WhenDifferentPaymentAlreadySet_ThrowsDomainConflictException()
    {
        var order = CreateOrder();
        var firstPayment = Payment.Create(order.Id, order.TotalAmount, FixedUtcNow);
        var secondPayment = Payment.Create(order.Id, order.TotalAmount, FixedUtcNow);
        order.SetPayment(firstPayment);

        var exception = Assert.Throws<DomainConflictException>(
            () => order.SetPayment(secondPayment));

        Assert.Equal("invalid_order_transition", exception.Code);
        Assert.Same(firstPayment, order.Payment);
    }

    [Fact]
    public void SetPayment_WithNullPayment_ThrowsDomainValidationException()
    {
        var order = CreateOrder();

        Assert.Throws<DomainValidationException>(() => order.SetPayment(null!));
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

    [Fact]
    public void Cancel_OnCreatedOrder_CreatesCancellationForRemainingQuantity()
    {
        var order = Order.Create(
            UserId,
            new[] { new OrderItemData(ProductA, "Widget", Money.FromDecimal(10m), 3) },
            FixedUtcNow);

        order.Cancel(FixedUtcNow);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        var item = Assert.Single(order.Items);
        Assert.Equal(3, item.CancelledQuantity);
        var cancellation = Assert.Single(item.Cancellations);
        Assert.Equal(3, cancellation.Quantity);
        Assert.Equal(FixedUtcNow, cancellation.CancelledAtUtc);
    }

    [Fact]
    public void Cancel_AfterPartialCancellation_CancelsOnlyRemainingQuantity()
    {
        var order = Order.Create(
            UserId,
            new[] { new OrderItemData(ProductA, "Widget", Money.FromDecimal(10m), 2) },
            FixedUtcNow);
        var item = Assert.Single(order.Items);
        order.CancelItems(new[] { (item.Id, 1) }, FixedUtcNow);

        order.Cancel(FixedUtcNow);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(2, item.CancelledQuantity);
        Assert.Equal(2, item.Cancellations.Count);
        Assert.Equal(2, item.Cancellations.Sum(cancellation => cancellation.Quantity));
    }

    [Fact]
    public void CancelItems_WithPartialQuantity_UpdatesCancelledQuantityAndKeepsStatusCreated()
    {
        var order = Order.Create(
            UserId,
            new[] { new OrderItemData(ProductA, "Widget", Money.FromDecimal(10m), 2) },
            FixedUtcNow);
        var item = Assert.Single(order.Items);

        order.CancelItems(new[] { (item.Id, 1) }, FixedUtcNow);

        Assert.Equal(OrderStatus.Created, order.Status);
        Assert.Equal(1, item.CancelledQuantity);
        var cancellation = Assert.Single(item.Cancellations);
        Assert.Equal(1, cancellation.Quantity);
        Assert.Equal(FixedUtcNow, cancellation.CancelledAtUtc);
        Assert.Equal(20m, order.TotalAmount.Amount);
    }

    [Fact]
    public void CancelItems_WhenAllRemainingQuantityCancelled_SetsStatusCancelled()
    {
        var order = Order.Create(
            UserId,
            new[] { new OrderItemData(ProductA, "Widget", Money.FromDecimal(10m), 2) },
            FixedUtcNow);
        var item = Assert.Single(order.Items);

        order.CancelItems(new[] { (item.Id, 2) }, FixedUtcNow);

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Equal(2, item.CancelledQuantity);
        Assert.Equal(10m * 2, order.TotalAmount.Amount);
    }

    [Fact]
    public void CancelItems_WhenQuantityExceedsRemaining_ThrowsDomainValidationException()
    {
        var order = Order.Create(
            UserId,
            new[] { new OrderItemData(ProductA, "Widget", Money.FromDecimal(10m), 3) },
            FixedUtcNow);
        var item = Assert.Single(order.Items);
        order.CancelItems(new[] { (item.Id, 1) }, FixedUtcNow);

        Assert.Throws<DomainValidationException>(
            () => order.CancelItems(new[] { (item.Id, 3) }, FixedUtcNow));

        Assert.Equal(1, item.CancelledQuantity);
        Assert.Equal(OrderStatus.Created, order.Status);
    }

    [Fact]
    public void CancelItems_WithUnknownOrderItemId_ThrowsDomainValidationException()
    {
        var order = CreateOrder();

        Assert.Throws<DomainValidationException>(
            () => order.CancelItems(new[] { (Guid.NewGuid(), 1) }, FixedUtcNow));
    }

    [Fact]
    public void CancelItems_WithDuplicateOrderItemIds_ThrowsDomainValidationException()
    {
        var order = CreateOrder();
        var item = Assert.Single(order.Items);

        Assert.Throws<DomainValidationException>(
            () => order.CancelItems(new[] { (item.Id, 1), (item.Id, 1) }, FixedUtcNow));

        Assert.Equal(0, item.CancelledQuantity);
    }

    [Fact]
    public void CancelItems_WithNullItems_ThrowsDomainValidationException()
    {
        var order = CreateOrder();

        Assert.Throws<DomainValidationException>(
            () => order.CancelItems(null!, FixedUtcNow));
    }

    [Fact]
    public void CancelItems_WithEmptyItems_ThrowsDomainValidationException()
    {
        var order = CreateOrder();

        Assert.Throws<DomainValidationException>(
            () => order.CancelItems(Array.Empty<(Guid OrderItemId, int Quantity)>(), FixedUtcNow));
    }

    [Fact]
    public void AddCancellation_WithNonPositiveQuantity_ThrowsDomainValidationException()
    {
        var order = CreateOrder();
        var item = Assert.Single(order.Items);

        Assert.Throws<DomainValidationException>(() => item.AddCancellation(0, FixedUtcNow));
        Assert.Throws<DomainValidationException>(() => item.AddCancellation(-1, FixedUtcNow));
    }

    [Fact]
    public void AddCancellation_WithQuantityExceedingRemaining_ThrowsDomainValidationException()
    {
        var order = CreateOrder();
        var item = Assert.Single(order.Items);
        item.AddCancellation(1, FixedUtcNow);

        Assert.Throws<DomainValidationException>(() => item.AddCancellation(1, FixedUtcNow));
        Assert.Equal(1, item.CancelledQuantity);
    }

    [Fact]
    public void Create_RaisesOrderPlaced()
    {
        var order = CreateOrder();

        var orderPlaced = Assert.Single(order.DomainEvents.OfType<OrderPlaced>());
        Assert.Equal(order.Id, orderPlaced.OrderId);
        Assert.Equal(UserId, orderPlaced.UserId);
        Assert.Equal(10m, orderPlaced.TotalAmount.Amount);
        Assert.Equal(1, orderPlaced.ItemCount);
        Assert.Equal(FixedUtcNow, orderPlaced.OccurredAtUtc);
    }

    [Fact]
    public void CancelItems_WithPartialQuantity_RaisesOrderCancelledWithNewItems()
    {
        var order = Order.Create(
            UserId,
            new[] { new OrderItemData(ProductA, "Widget", Money.FromDecimal(10m), 2) },
            FixedUtcNow);
        var item = Assert.Single(order.Items);
        order.ClearDomainEvents();

        order.CancelItems(new[] { (item.Id, 1) }, FixedUtcNow);

        var orderCancelled = Assert.Single(order.DomainEvents.OfType<OrderCancelled>());
        Assert.False(orderCancelled.IsFullCancellation);
        var cancelledItem = Assert.Single(orderCancelled.Items);
        Assert.Equal(item.Id, cancelledItem.OrderItemId);
        Assert.Equal(ProductA, cancelledItem.ProductId);
        Assert.Equal(1, cancelledItem.Quantity);
        Assert.Equal(FixedUtcNow, orderCancelled.OccurredAtUtc);
    }

    [Fact]
    public void CancelItems_AfterPreviousCancellation_RaisesOnlyNewlyCancelledItems()
    {
        var order = Order.Create(
            UserId,
            new[] { new OrderItemData(ProductA, "Widget", Money.FromDecimal(10m), 3) },
            FixedUtcNow);
        var item = Assert.Single(order.Items);
        order.CancelItems(new[] { (item.Id, 1) }, FixedUtcNow);
        order.ClearDomainEvents();

        order.CancelItems(new[] { (item.Id, 1) }, FixedUtcNow);

        var orderCancelled = Assert.Single(order.DomainEvents.OfType<OrderCancelled>());
        Assert.False(orderCancelled.IsFullCancellation);
        var cancelledItem = Assert.Single(orderCancelled.Items);
        Assert.Equal(item.Id, cancelledItem.OrderItemId);
        Assert.Equal(1, cancelledItem.Quantity);
    }

    [Fact]
    public void CancelItems_WhenAllRemainingQuantityCancelled_RaisesFullOrderCancelled()
    {
        var order = Order.Create(
            UserId,
            new[] { new OrderItemData(ProductA, "Widget", Money.FromDecimal(10m), 2) },
            FixedUtcNow);
        var item = Assert.Single(order.Items);
        order.ClearDomainEvents();

        order.CancelItems(new[] { (item.Id, 2) }, FixedUtcNow);

        var orderCancelled = Assert.Single(order.DomainEvents.OfType<OrderCancelled>());
        Assert.True(orderCancelled.IsFullCancellation);
    }

    [Fact]
    public void Cancel_RaisesSingleOrderCancelled()
    {
        var order = CreateOrder();
        order.ClearDomainEvents();

        order.Cancel(FixedUtcNow);

        Assert.Single(order.DomainEvents.OfType<OrderCancelled>());
    }

    [Fact]
    public void MarkPaid_RaisesOrderPaid()
    {
        var order = CreateOrder(Guid.NewGuid());
        order.ClearDomainEvents();

        order.MarkPaid(FixedUtcNow);

        var orderPaid = Assert.Single(order.DomainEvents.OfType<OrderPaid>());
        Assert.Equal(order.Id, orderPaid.OrderId);
        Assert.Equal(UserId, orderPaid.UserId);
        Assert.Equal(FixedUtcNow, orderPaid.OccurredAtUtc);
    }

    [Fact]
    public void Ship_RaisesOrderShipped()
    {
        var order = CreatePaidOrder();
        order.ClearDomainEvents();

        order.Ship(FixedUtcNow);

        Assert.Single(order.DomainEvents.OfType<OrderShipped>());
    }

    [Fact]
    public void Deliver_RaisesOrderDelivered()
    {
        var order = CreateShippedOrder();
        order.ClearDomainEvents();

        order.Deliver(FixedUtcNow);

        Assert.Single(order.DomainEvents.OfType<OrderDelivered>());
    }

    [Fact]
    public void Return_RaisesOrderReturned()
    {
        var order = CreateDeliveredOrder();
        order.ClearDomainEvents();

        order.Return(FixedUtcNow);

        Assert.Single(order.DomainEvents.OfType<OrderReturned>());
    }

    [Fact]
    public void ClearDomainEvents_RemovesAllEvents()
    {
        var order = CreateOrder();
        Assert.NotEmpty(order.DomainEvents);

        order.ClearDomainEvents();

        Assert.Empty(order.DomainEvents);
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

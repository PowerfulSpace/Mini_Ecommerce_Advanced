using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Notifications;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Infrastructure.Notifications;

namespace PS.MiniEcommerce.UnitTests.Application.Notifications;

public class NotificationSubscriberTests
{
    private static readonly DateTime OccurredAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly InMemoryNotificationStore _notificationStore =
        new(NullLogger<InMemoryNotificationStore>.Instance);

    [Fact]
    public async Task Handle_OrderPlaced_CreatesNotification()
    {
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var before = DateTime.UtcNow;

        var notification = await HandleAsync(new OrderPlaced(
            orderId,
            userId,
            Money.FromDecimal(19.99m),
            2,
            OccurredAtUtc));

        var after = DateTime.UtcNow;

        Assert.Equal(nameof(OrderPlaced), notification.EventType);
        Assert.Equal(orderId, notification.EntityId);
        Assert.Equal(orderId, notification.OrderId);
        Assert.Equal(userId, notification.UserId);
        Assert.Equal(OccurredAtUtc, notification.OccurredAtUtc);
        Assert.InRange(notification.CreatedAtUtc, before, after);
        Assert.Contains(orderId.ToString(), notification.Message);
        Assert.Contains("placed", notification.Message);
        Assert.NotNull(notification.Amount);
        Assert.Equal(19.99m, notification.Amount.Value);
    }

    [Fact]
    public async Task Handle_OrderShipped_CreatesNotification()
    {
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var notification = await HandleAsync(new OrderShipped(orderId, userId, OccurredAtUtc));

        Assert.Equal(nameof(OrderShipped), notification.EventType);
        Assert.Equal(orderId, notification.EntityId);
        Assert.Equal(orderId, notification.OrderId);
        Assert.Equal(userId, notification.UserId);
        Assert.Equal(OccurredAtUtc, notification.OccurredAtUtc);
        Assert.Contains("shipped", notification.Message);
        Assert.Null(notification.Amount);
    }

    [Fact]
    public async Task Handle_OrderDelivered_CreatesNotification()
    {
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var notification = await HandleAsync(new OrderDelivered(orderId, userId, OccurredAtUtc));

        Assert.Equal(nameof(OrderDelivered), notification.EventType);
        Assert.Equal(orderId, notification.EntityId);
        Assert.Equal(orderId, notification.OrderId);
        Assert.Equal(userId, notification.UserId);
        Assert.Equal(OccurredAtUtc, notification.OccurredAtUtc);
        Assert.Contains("delivered", notification.Message);
        Assert.Null(notification.Amount);
    }

    [Fact]
    public async Task Handle_OrderReturned_CreatesNotification()
    {
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var notification = await HandleAsync(new OrderReturned(orderId, userId, OccurredAtUtc));

        Assert.Equal(nameof(OrderReturned), notification.EventType);
        Assert.Equal(orderId, notification.EntityId);
        Assert.Equal(orderId, notification.OrderId);
        Assert.Equal(userId, notification.UserId);
        Assert.Equal(OccurredAtUtc, notification.OccurredAtUtc);
        Assert.Contains("returned", notification.Message);
        Assert.Null(notification.Amount);
    }

    [Theory]
    [InlineData(true, 2, "full cancellation: True", "items: 2")]
    [InlineData(false, 1, "full cancellation: False", "items: 1")]
    public async Task Handle_OrderCancelled_CreatesNotificationWithCancellationDetails(
        bool isFullCancellation,
        int itemCount,
        string fullCancellationText,
        string itemsText)
    {
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var items = Enumerable.Range(0, itemCount)
            .Select(_ => new CancelledItem(Guid.NewGuid(), Guid.NewGuid(), 1))
            .ToList();

        var notification = await HandleAsync(new OrderCancelled(
            orderId,
            userId,
            isFullCancellation,
            items,
            OccurredAtUtc));

        Assert.Equal(nameof(OrderCancelled), notification.EventType);
        Assert.Equal(orderId, notification.EntityId);
        Assert.Equal(orderId, notification.OrderId);
        Assert.Equal(userId, notification.UserId);
        Assert.Equal(OccurredAtUtc, notification.OccurredAtUtc);
        Assert.Contains(fullCancellationText, notification.Message);
        Assert.Contains(itemsText, notification.Message);
        Assert.Null(notification.Amount);
    }

    [Fact]
    public async Task Handle_PaymentConfirmed_LooksUpOrderUserAndCreatesNotification()
    {
        var paymentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _orderRepository
            .GetOrderUserIdAsync(orderId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(userId));

        var notification = await HandleAsync(new PaymentConfirmed(
            paymentId,
            orderId,
            Money.FromDecimal(19.99m),
            OccurredAtUtc));

        Assert.Equal(nameof(PaymentConfirmed), notification.EventType);
        Assert.Equal(paymentId, notification.EntityId);
        Assert.Equal(orderId, notification.OrderId);
        Assert.Equal(userId, notification.UserId);
        Assert.Equal(OccurredAtUtc, notification.OccurredAtUtc);
        Assert.Contains(paymentId.ToString(), notification.Message);
        Assert.Contains("confirmed", notification.Message);
        Assert.NotNull(notification.Amount);
        Assert.Equal(19.99m, notification.Amount.Value);
        await _orderRepository.Received(1)
            .GetOrderUserIdAsync(orderId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PaymentRefunded_LooksUpOrderUserAndCreatesNotification()
    {
        var paymentId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _orderRepository
            .GetOrderUserIdAsync(orderId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(userId));

        var notification = await HandleAsync(new PaymentRefunded(
            paymentId,
            orderId,
            Money.FromDecimal(5m),
            OccurredAtUtc,
            "customer_request"));

        Assert.Equal(nameof(PaymentRefunded), notification.EventType);
        Assert.Equal(paymentId, notification.EntityId);
        Assert.Equal(orderId, notification.OrderId);
        Assert.Equal(userId, notification.UserId);
        Assert.Equal(OccurredAtUtc, notification.OccurredAtUtc);
        Assert.Contains(paymentId.ToString(), notification.Message);
        Assert.Contains("refunded", notification.Message);
        Assert.NotNull(notification.Amount);
        Assert.Equal(5m, notification.Amount.Value);
        await _orderRepository.Received(1)
            .GetOrderUserIdAsync(orderId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PaymentEventWhenOrderMissing_DoesNotCreateNotification()
    {
        var orderId = Guid.NewGuid();
        _orderRepository
            .GetOrderUserIdAsync(orderId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(null));

        await CreateSubscriber().HandleAsync(
            new PaymentConfirmed(
                Guid.NewGuid(),
                orderId,
                Money.FromDecimal(19.99m),
                OccurredAtUtc),
            CancellationToken.None);

        Assert.Empty(_notificationStore.GetAll());
        await _orderRepository.Received(1)
            .GetOrderUserIdAsync(orderId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OrderPaid_DoesNotCreateNotification()
    {
        await CreateSubscriber().HandleAsync(
            new OrderPaid(Guid.NewGuid(), Guid.NewGuid(), OccurredAtUtc),
            CancellationToken.None);

        Assert.Empty(_notificationStore.GetAll());
    }

    [Fact]
    public async Task Handle_StockAdjusted_DoesNotCreateNotification()
    {
        await CreateSubscriber().HandleAsync(
            new StockAdjusted(Guid.NewGuid(), 10, OccurredAtUtc),
            CancellationToken.None);

        Assert.Empty(_notificationStore.GetAll());
    }

    [Fact]
    public async Task Handle_ProductDeactivated_DoesNotCreateNotification()
    {
        await CreateSubscriber().HandleAsync(
            new ProductDeactivated(Guid.NewGuid(), OccurredAtUtc),
            CancellationToken.None);

        Assert.Empty(_notificationStore.GetAll());
    }

    [Fact]
    public async Task Handle_UnknownEvent_DoesNotCreateNotification()
    {
        await CreateSubscriber().HandleAsync(new object(), CancellationToken.None);

        Assert.Empty(_notificationStore.GetAll());
    }

    private async Task<Notification> HandleAsync(object domainEvent)
    {
        await CreateSubscriber().HandleAsync(domainEvent, CancellationToken.None);

        return Assert.Single(_notificationStore.GetAll());
    }

    private NotificationSubscriber CreateSubscriber() =>
        new(
            _orderRepository,
            _notificationStore,
            NullLogger<NotificationSubscriber>.Instance);
}

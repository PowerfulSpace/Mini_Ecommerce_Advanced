using Microsoft.Extensions.Logging;
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
    private const string Email = "user@example.com";

    private static readonly DateTime OccurredAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly IOrderRepository _orderRepository = Substitute.For<IOrderRepository>();
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly INotificationSender _notificationSender = Substitute.For<INotificationSender>();
    private readonly InMemoryNotificationStore _notificationStore =
        new(NullLogger<InMemoryNotificationStore>.Instance);
    private readonly RecordingLogger<NotificationSubscriber> _logger = new();

    public NotificationSubscriberTests()
    {
        _userRepository
            .GetEmailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(Email));
    }

    [Fact]
    public async Task Handle_OrderPlaced_CreatesNotificationAndSendsEmail()
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
        await _notificationSender.Received(1)
            .SendAsync(Email, notification, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OrderShipped_CreatesNotificationAndSendsEmail()
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
        await _notificationSender.Received(1)
            .SendAsync(Email, notification, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OrderDelivered_CreatesNotificationAndSendsEmail()
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
        await _notificationSender.Received(1)
            .SendAsync(Email, notification, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OrderReturned_CreatesNotificationAndSendsEmail()
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
        await _notificationSender.Received(1)
            .SendAsync(Email, notification, Arg.Any<CancellationToken>());
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
        await _notificationSender.Received(1)
            .SendAsync(Email, notification, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PaymentConfirmed_LooksUpOrderUserAndSendsEmail()
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
        await _userRepository.Received(1)
            .GetEmailAsync(userId, Arg.Any<CancellationToken>());
        await _notificationSender.Received(1)
            .SendAsync(Email, notification, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PaymentRefunded_LooksUpOrderUserAndSendsEmail()
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
        await _userRepository.Received(1)
            .GetEmailAsync(userId, Arg.Any<CancellationToken>());
        await _notificationSender.Received(1)
            .SendAsync(Email, notification, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PaymentEventWhenOrderMissing_DoesNotCreateNotificationOrSend()
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
        await _userRepository.DidNotReceive()
            .GetEmailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _notificationSender.DidNotReceive()
            .SendAsync(Arg.Any<string>(), Arg.Any<Notification>(), Arg.Any<CancellationToken>());
        await _orderRepository.Received(1)
            .GetOrderUserIdAsync(orderId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenEmailIsMissing_DoesNotStoreOrSend()
    {
        _userRepository
            .GetEmailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));

        await CreateSubscriber().HandleAsync(
            new OrderShipped(Guid.NewGuid(), Guid.NewGuid(), OccurredAtUtc),
            CancellationToken.None);

        Assert.Empty(_notificationStore.GetAll());
        await _notificationSender.DidNotReceive()
            .SendAsync(Arg.Any<string>(), Arg.Any<Notification>(), Arg.Any<CancellationToken>());
        Assert.Contains(
            _logger.Messages,
            message => message.Level == LogLevel.Warning && message.Message.Contains("email"));
    }

    [Fact]
    public async Task Handle_WhenEmailIsEmpty_DoesNotStoreOrSend()
    {
        _userRepository
            .GetEmailAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("   "));

        await CreateSubscriber().HandleAsync(
            new OrderShipped(Guid.NewGuid(), Guid.NewGuid(), OccurredAtUtc),
            CancellationToken.None);

        Assert.Empty(_notificationStore.GetAll());
        await _notificationSender.DidNotReceive()
            .SendAsync(Arg.Any<string>(), Arg.Any<Notification>(), Arg.Any<CancellationToken>());
        Assert.Contains(
            _logger.Messages,
            message => message.Level == LogLevel.Warning && message.Message.Contains("email"));
    }

    [Fact]
    public async Task Handle_PaymentEventWhenEmailIsMissing_DoesNotStoreOrSend()
    {
        var orderId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        _orderRepository
            .GetOrderUserIdAsync(orderId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(userId));
        _userRepository
            .GetEmailAsync(userId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));

        await CreateSubscriber().HandleAsync(
            new PaymentRefunded(
                Guid.NewGuid(),
                orderId,
                Money.FromDecimal(5m),
                OccurredAtUtc),
            CancellationToken.None);

        Assert.Empty(_notificationStore.GetAll());
        await _notificationSender.DidNotReceive()
            .SendAsync(Arg.Any<string>(), Arg.Any<Notification>(), Arg.Any<CancellationToken>());
        Assert.Contains(
            _logger.Messages,
            message => message.Level == LogLevel.Warning && message.Message.Contains("email"));
    }

    [Fact]
    public async Task Handle_WhenNotificationAlreadyStored_SendsAgainAndKeepsSingleStoreEntry()
    {
        var domainEvent = new OrderShipped(Guid.NewGuid(), Guid.NewGuid(), OccurredAtUtc);
        var subscriber = CreateSubscriber();

        await subscriber.HandleAsync(domainEvent, CancellationToken.None);
        await subscriber.HandleAsync(domainEvent, CancellationToken.None);

        Assert.Single(_notificationStore.GetAll());
        await _notificationSender.Received(2)
            .SendAsync(
                Email,
                Arg.Is<Notification>(notification => notification.EventType == nameof(OrderShipped)),
                Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SendsEmailBeforeStoringNotification()
    {
        var store = Substitute.For<INotificationStore>();
        store.TryAdd(Arg.Any<Notification>()).Returns(true);
        var subscriber = new NotificationSubscriber(
            _orderRepository,
            _userRepository,
            store,
            _notificationSender,
            _logger);

        await subscriber.HandleAsync(
            new OrderShipped(Guid.NewGuid(), Guid.NewGuid(), OccurredAtUtc),
            CancellationToken.None);

        Received.InOrder(() =>
        {
            _notificationSender.SendAsync(
                Email,
                Arg.Is<Notification>(notification => notification.EventType == nameof(OrderShipped)),
                Arg.Any<CancellationToken>());
            store.TryAdd(Arg.Any<Notification>());
        });
    }

    [Fact]
    public async Task Handle_WhenSendAsyncThrows_DoesNotStoreNotificationAndPropagates()
    {
        _notificationSender
            .SendAsync(Arg.Any<string>(), Arg.Any<Notification>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("SMTP failure")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateSubscriber().HandleAsync(
                new OrderShipped(Guid.NewGuid(), Guid.NewGuid(), OccurredAtUtc),
                CancellationToken.None));

        Assert.Equal("SMTP failure", exception.Message);
        Assert.Empty(_notificationStore.GetAll());
    }

    [Fact]
    public async Task Handle_WhenTryAddReturnsFalse_DoesNotThrowAndStillSends()
    {
        var store = Substitute.For<INotificationStore>();
        store.TryAdd(Arg.Any<Notification>()).Returns(false);
        var subscriber = new NotificationSubscriber(
            _orderRepository,
            _userRepository,
            store,
            _notificationSender,
            _logger);

        await subscriber.HandleAsync(
            new OrderShipped(Guid.NewGuid(), Guid.NewGuid(), OccurredAtUtc),
            CancellationToken.None);

        await _notificationSender.Received(1)
            .SendAsync(
                Email,
                Arg.Is<Notification>(notification => notification.EventType == nameof(OrderShipped)),
                Arg.Any<CancellationToken>());
        store.Received(1).TryAdd(Arg.Any<Notification>());
    }

    [Fact]
    public async Task Handle_OrderPaid_DoesNotCreateNotificationOrSend()
    {
        await CreateSubscriber().HandleAsync(
            new OrderPaid(Guid.NewGuid(), Guid.NewGuid(), OccurredAtUtc),
            CancellationToken.None);

        Assert.Empty(_notificationStore.GetAll());
        await _notificationSender.DidNotReceive()
            .SendAsync(Arg.Any<string>(), Arg.Any<Notification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StockAdjusted_DoesNotCreateNotificationOrSend()
    {
        await CreateSubscriber().HandleAsync(
            new StockAdjusted(Guid.NewGuid(), 10, OccurredAtUtc),
            CancellationToken.None);

        Assert.Empty(_notificationStore.GetAll());
        await _notificationSender.DidNotReceive()
            .SendAsync(Arg.Any<string>(), Arg.Any<Notification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ProductDeactivated_DoesNotCreateNotificationOrSend()
    {
        await CreateSubscriber().HandleAsync(
            new ProductDeactivated(Guid.NewGuid(), OccurredAtUtc),
            CancellationToken.None);

        Assert.Empty(_notificationStore.GetAll());
        await _notificationSender.DidNotReceive()
            .SendAsync(Arg.Any<string>(), Arg.Any<Notification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownEvent_DoesNotCreateNotificationOrSend()
    {
        await CreateSubscriber().HandleAsync(new object(), CancellationToken.None);

        Assert.Empty(_notificationStore.GetAll());
        await _notificationSender.DidNotReceive()
            .SendAsync(Arg.Any<string>(), Arg.Any<Notification>(), Arg.Any<CancellationToken>());
    }

    private async Task<Notification> HandleAsync(object domainEvent)
    {
        await CreateSubscriber().HandleAsync(domainEvent, CancellationToken.None);

        return Assert.Single(_notificationStore.GetAll());
    }

    private NotificationSubscriber CreateSubscriber() =>
        new(
            _orderRepository,
            _userRepository,
            _notificationStore,
            _notificationSender,
            _logger);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add((logLevel, formatter(state, exception)));
        }
    }
}

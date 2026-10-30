using Microsoft.Extensions.Logging;
using PS.MiniEcommerce.Application.Abstractions.Outbox;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Domain.Events;

namespace PS.MiniEcommerce.Application.Notifications;

public sealed class NotificationSubscriber : IOutboxSubscriber
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUserRepository _userRepository;
    private readonly INotificationStore _notificationStore;
    private readonly INotificationSender _notificationSender;
    private readonly ILogger<NotificationSubscriber> _logger;

    public NotificationSubscriber(
        IOrderRepository orderRepository,
        IUserRepository userRepository,
        INotificationStore notificationStore,
        INotificationSender notificationSender,
        ILogger<NotificationSubscriber> logger)
    {
        _orderRepository = orderRepository;
        _userRepository = userRepository;
        _notificationStore = notificationStore;
        _notificationSender = notificationSender;
        _logger = logger;
    }

    public async Task HandleAsync(object domainEvent, CancellationToken cancellationToken)
    {
        switch (domainEvent)
        {
            case OrderPlaced orderPlaced:
                await AddNotificationAsync(
                    nameof(OrderPlaced),
                    orderPlaced.OrderId,
                    orderPlaced.OrderId,
                    orderPlaced.UserId,
                    orderPlaced.OccurredAtUtc,
                    $"Order {orderPlaced.OrderId} placed",
                    orderPlaced.TotalAmount.Amount,
                    cancellationToken);
                break;

            case OrderShipped orderShipped:
                await AddNotificationAsync(
                    nameof(OrderShipped),
                    orderShipped.OrderId,
                    orderShipped.OrderId,
                    orderShipped.UserId,
                    orderShipped.OccurredAtUtc,
                    $"Order {orderShipped.OrderId} shipped",
                    null,
                    cancellationToken);
                break;

            case OrderDelivered orderDelivered:
                await AddNotificationAsync(
                    nameof(OrderDelivered),
                    orderDelivered.OrderId,
                    orderDelivered.OrderId,
                    orderDelivered.UserId,
                    orderDelivered.OccurredAtUtc,
                    $"Order {orderDelivered.OrderId} delivered",
                    null,
                    cancellationToken);
                break;

            case OrderCancelled orderCancelled:
                await AddNotificationAsync(
                    nameof(OrderCancelled),
                    orderCancelled.OrderId,
                    orderCancelled.OrderId,
                    orderCancelled.UserId,
                    orderCancelled.OccurredAtUtc,
                    $"Order {orderCancelled.OrderId} cancelled (full cancellation: {orderCancelled.IsFullCancellation}, items: {orderCancelled.Items.Count})",
                    null,
                    cancellationToken);
                break;

            case OrderReturned orderReturned:
                await AddNotificationAsync(
                    nameof(OrderReturned),
                    orderReturned.OrderId,
                    orderReturned.OrderId,
                    orderReturned.UserId,
                    orderReturned.OccurredAtUtc,
                    $"Order {orderReturned.OrderId} returned",
                    null,
                    cancellationToken);
                break;

            case PaymentConfirmed paymentConfirmed:
                await AddPaymentNotificationAsync(
                    nameof(PaymentConfirmed),
                    paymentConfirmed.PaymentId,
                    paymentConfirmed.OrderId,
                    paymentConfirmed.OccurredAtUtc,
                    $"Payment {paymentConfirmed.PaymentId} confirmed",
                    paymentConfirmed.Amount.Amount,
                    cancellationToken);
                break;

            case PaymentRefunded paymentRefunded:
                await AddPaymentNotificationAsync(
                    nameof(PaymentRefunded),
                    paymentRefunded.PaymentId,
                    paymentRefunded.OrderId,
                    paymentRefunded.OccurredAtUtc,
                    $"Payment {paymentRefunded.PaymentId} refunded",
                    paymentRefunded.Amount.Amount,
                    cancellationToken);
                break;

            default:
                _logger.LogDebug(
                    "No notification configured for domain event {EventType}.",
                    domainEvent.GetType().Name);
                break;
        }
    }

    private async Task AddNotificationAsync(
        string eventType,
        Guid entityId,
        Guid? orderId,
        Guid userId,
        DateTime occurredAtUtc,
        string message,
        decimal? amount,
        CancellationToken cancellationToken)
    {
        var email = await _userRepository.GetEmailAsync(userId, cancellationToken);

        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning(
                "Cannot send notification {EventType} for entity {EntityId}: email for user {UserId} was not found.",
                eventType,
                entityId,
                userId);
            return;
        }

        var notification = new Notification(
            eventType,
            entityId,
            orderId,
            userId,
            DateTime.UtcNow,
            occurredAtUtc,
            message,
            amount);

        if (!_notificationStore.TryAdd(notification))
        {
            return;
        }

        await _notificationSender.SendAsync(email, notification, cancellationToken);
    }

    private async Task AddPaymentNotificationAsync(
        string eventType,
        Guid paymentId,
        Guid orderId,
        DateTime occurredAtUtc,
        string message,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var userId = await _orderRepository.GetOrderUserIdAsync(orderId, cancellationToken);

        if (userId is null)
        {
            _logger.LogWarning(
                "Cannot create notification {EventType} for payment {PaymentId}: order {OrderId} was not found.",
                eventType,
                paymentId,
                orderId);
            return;
        }

        await AddNotificationAsync(
            eventType,
            paymentId,
            orderId,
            userId.Value,
            occurredAtUtc,
            message,
            amount,
            cancellationToken);
    }
}

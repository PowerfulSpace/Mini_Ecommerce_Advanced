using Microsoft.Extensions.Logging;
using PS.MiniEcommerce.Application.Abstractions.Outbox;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Domain.Events;

namespace PS.MiniEcommerce.Application.Notifications;

public sealed class NotificationSubscriber : IOutboxSubscriber
{
    private readonly IOrderRepository _orderRepository;
    private readonly INotificationStore _notificationStore;
    private readonly ILogger<NotificationSubscriber> _logger;

    public NotificationSubscriber(
        IOrderRepository orderRepository,
        INotificationStore notificationStore,
        ILogger<NotificationSubscriber> logger)
    {
        _orderRepository = orderRepository;
        _notificationStore = notificationStore;
        _logger = logger;
    }

    public async Task HandleAsync(object domainEvent, CancellationToken cancellationToken)
    {
        switch (domainEvent)
        {
            case OrderPlaced orderPlaced:
                AddNotification(
                    nameof(OrderPlaced),
                    orderPlaced.OrderId,
                    orderPlaced.OrderId,
                    orderPlaced.UserId,
                    orderPlaced.OccurredAtUtc,
                    $"Order {orderPlaced.OrderId} placed",
                    orderPlaced.TotalAmount.Amount);
                break;

            case OrderShipped orderShipped:
                AddNotification(
                    nameof(OrderShipped),
                    orderShipped.OrderId,
                    orderShipped.OrderId,
                    orderShipped.UserId,
                    orderShipped.OccurredAtUtc,
                    $"Order {orderShipped.OrderId} shipped",
                    null);
                break;

            case OrderDelivered orderDelivered:
                AddNotification(
                    nameof(OrderDelivered),
                    orderDelivered.OrderId,
                    orderDelivered.OrderId,
                    orderDelivered.UserId,
                    orderDelivered.OccurredAtUtc,
                    $"Order {orderDelivered.OrderId} delivered",
                    null);
                break;

            case OrderCancelled orderCancelled:
                AddNotification(
                    nameof(OrderCancelled),
                    orderCancelled.OrderId,
                    orderCancelled.OrderId,
                    orderCancelled.UserId,
                    orderCancelled.OccurredAtUtc,
                    $"Order {orderCancelled.OrderId} cancelled (full cancellation: {orderCancelled.IsFullCancellation}, items: {orderCancelled.Items.Count})",
                    null);
                break;

            case OrderReturned orderReturned:
                AddNotification(
                    nameof(OrderReturned),
                    orderReturned.OrderId,
                    orderReturned.OrderId,
                    orderReturned.UserId,
                    orderReturned.OccurredAtUtc,
                    $"Order {orderReturned.OrderId} returned",
                    null);
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

    private void AddNotification(
        string eventType,
        Guid entityId,
        Guid? orderId,
        Guid userId,
        DateTime occurredAtUtc,
        string message,
        decimal? amount)
    {
        _notificationStore.Add(new Notification(
            eventType,
            entityId,
            orderId,
            userId,
            DateTime.UtcNow,
            occurredAtUtc,
            message,
            amount));
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

        AddNotification(
            eventType,
            paymentId,
            orderId,
            userId.Value,
            occurredAtUtc,
            message,
            amount);
    }
}

using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Domain.Events;

namespace PS.MiniEcommerce.Application.Events.Handlers;

public sealed class AuditHandler : INotificationHandler<DomainEventNotification<object>>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuditHandler> _logger;

    public AuditHandler(IServiceScopeFactory scopeFactory, ILogger<AuditHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Handle(
        DomainEventNotification<object> notification,
        CancellationToken cancellationToken)
    {
        if (notification.Event is null)
        {
            _logger.LogWarning("Received a null domain event.");
            return;
        }

        var entry = notification.Event switch
        {
            OrderPlaced orderPlaced => CreateOrderPlacedEntry(orderPlaced),
            OrderCancelled orderCancelled => CreateOrderCancelledEntry(orderCancelled),
            OrderShipped orderShipped => CreateOrderTransitionEntry(
                orderShipped.OrderId,
                "OrderShipped",
                orderShipped.OccurredAtUtc,
                GetActorUserId()),
            OrderDelivered orderDelivered => CreateOrderTransitionEntry(
                orderDelivered.OrderId,
                "OrderDelivered",
                orderDelivered.OccurredAtUtc,
                GetActorUserId()),
            OrderReturned orderReturned => CreateOrderTransitionEntry(
                orderReturned.OrderId,
                "OrderReturned",
                orderReturned.OccurredAtUtc,
                GetActorUserId()),
            OrderPaid orderPaid => CreateOrderTransitionEntry(
                orderPaid.OrderId,
                "OrderPaid",
                orderPaid.OccurredAtUtc,
                orderPaid.UserId),
            PaymentConfirmed paymentConfirmed => CreatePaymentConfirmedEntry(
                paymentConfirmed,
                GetActorUserId()),
            PaymentRefunded paymentRefunded => CreatePaymentRefundedEntry(
                paymentRefunded,
                GetActorUserId()),
            ProductDeactivated productDeactivated => CreateProductDeactivatedEntry(
                productDeactivated,
                GetActorUserId()),
            StockAdjusted stockAdjusted => CreateStockAdjustedEntry(
                stockAdjusted,
                GetActorUserId()),
            _ => null
        };

        if (entry is null)
        {
            _logger.LogWarning(
                "No audit mapping for domain event {DomainEventType}.",
                notification.Event.GetType().Name);
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();
        await repository.AddAsync(entry, cancellationToken);
    }

    private static AuditLogEntry CreateOrderPlacedEntry(OrderPlaced orderPlaced)
    {
        var payload = JsonSerializer.Serialize(
            new
            {
                totalAmount = orderPlaced.TotalAmount.Amount,
                itemCount = orderPlaced.ItemCount
            },
            JsonOptions);

        return new AuditLogEntry(
            orderPlaced.UserId,
            "OrderPlaced",
            "Order",
            orderPlaced.OrderId,
            payload,
            orderPlaced.OccurredAtUtc,
            CorrelationId: null);
    }

    private static AuditLogEntry CreateOrderCancelledEntry(OrderCancelled orderCancelled)
    {
        var payload = JsonSerializer.Serialize(
            new
            {
                isFullCancellation = orderCancelled.IsFullCancellation,
                items = orderCancelled.Items.Select(item => new
                {
                    orderItemId = item.OrderItemId,
                    productId = item.ProductId,
                    quantity = item.Quantity
                }).ToArray()
            },
            JsonOptions);

        return new AuditLogEntry(
            orderCancelled.UserId,
            "OrderCancelled",
            "Order",
            orderCancelled.OrderId,
            payload,
            orderCancelled.OccurredAtUtc,
            CorrelationId: null);
    }

    private static AuditLogEntry CreateOrderTransitionEntry(
        Guid orderId,
        string action,
        DateTime occurredAtUtc,
        Guid? actorUserId)
    {
        return new AuditLogEntry(
            actorUserId,
            action,
            "Order",
            orderId,
            Payload: null,
            occurredAtUtc,
            CorrelationId: null);
    }

    private static AuditLogEntry CreatePaymentConfirmedEntry(
        PaymentConfirmed paymentConfirmed,
        Guid? actorUserId)
    {
        var payload = JsonSerializer.Serialize(
            new
            {
                orderId = paymentConfirmed.OrderId,
                amount = paymentConfirmed.Amount.Amount
            },
            JsonOptions);

        return new AuditLogEntry(
            actorUserId,
            "PaymentConfirmed",
            "Payment",
            paymentConfirmed.PaymentId,
            payload,
            paymentConfirmed.OccurredAtUtc,
            CorrelationId: null);
    }

    private static AuditLogEntry CreatePaymentRefundedEntry(
        PaymentRefunded paymentRefunded,
        Guid? actorUserId)
    {
        var payload = JsonSerializer.Serialize(
            new
            {
                orderId = paymentRefunded.OrderId,
                amount = paymentRefunded.Amount.Amount,
                reason = paymentRefunded.Reason
            },
            JsonOptions);

        return new AuditLogEntry(
            actorUserId,
            "PaymentRefunded",
            "Payment",
            paymentRefunded.PaymentId,
            payload,
            paymentRefunded.OccurredAtUtc,
            CorrelationId: null);
    }

    private static AuditLogEntry CreateProductDeactivatedEntry(
        ProductDeactivated productDeactivated,
        Guid? actorUserId)
    {
        return new AuditLogEntry(
            actorUserId,
            "ProductDeactivated",
            "Product",
            productDeactivated.ProductId,
            Payload: null,
            productDeactivated.OccurredAtUtc,
            CorrelationId: null);
    }

    private static AuditLogEntry CreateStockAdjustedEntry(
        StockAdjusted stockAdjusted,
        Guid? actorUserId)
    {
        var payload = JsonSerializer.Serialize(
            new
            {
                newAvailable = stockAdjusted.NewAvailable
            },
            JsonOptions);

        return new AuditLogEntry(
            actorUserId,
            "StockAdjusted",
            "Product",
            stockAdjusted.ProductId,
            payload,
            stockAdjusted.OccurredAtUtc,
            CorrelationId: null);
    }

    private Guid? GetActorUserId()
    {
        using var scope = _scopeFactory.CreateScope();
        var currentUser = scope.ServiceProvider.GetService<ICurrentUser>();

        try
        {
            return currentUser?.UserId;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

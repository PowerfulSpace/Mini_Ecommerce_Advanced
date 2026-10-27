using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
}

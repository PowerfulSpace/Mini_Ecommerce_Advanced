using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using PS.MiniEcommerce.Application.Notifications;

namespace PS.MiniEcommerce.Infrastructure.Notifications;

public sealed class InMemoryNotificationStore : INotificationStore
{
    private readonly ConcurrentDictionary<(string EventType, Guid EntityId, DateTime OccurredAtUtc), Notification> _notifications = new();

    private readonly ILogger<InMemoryNotificationStore> _logger;

    public InMemoryNotificationStore(ILogger<InMemoryNotificationStore> logger)
    {
        _logger = logger;
    }

    public void Add(Notification notification)
    {
        var key = (notification.EventType, notification.EntityId, notification.OccurredAtUtc);

        if (!_notifications.TryAdd(key, notification))
        {
            _logger.LogDebug(
                "Duplicate notification {EventType} for entity {EntityId} at {OccurredAtUtc} ignored.",
                notification.EventType,
                notification.EntityId,
                notification.OccurredAtUtc);
            return;
        }

        _logger.LogInformation(
            "Stored notification {EventType} for entity {EntityId} and user {UserId}: {Message}",
            notification.EventType,
            notification.EntityId,
            notification.UserId,
            notification.Message);
    }

    public IReadOnlyList<Notification> GetAll() =>
        _notifications.Values.ToList();
}

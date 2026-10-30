using System.Collections.Concurrent;
using PS.MiniEcommerce.Application.Notifications;

namespace PS.MiniEcommerce.IntegrationTests;

public sealed class TestNotificationStore : INotificationStore
{
    private readonly ConcurrentDictionary<(string EventType, Guid EntityId, DateTime OccurredAtUtc), Notification> _notifications = new();

    public bool TryAdd(Notification notification) =>
        _notifications.TryAdd(
            (notification.EventType, notification.EntityId, notification.OccurredAtUtc),
            notification);

    public IReadOnlyList<Notification> GetAll() => _notifications.Values.ToList();

    public void Clear() => _notifications.Clear();
}

using System.Collections.Concurrent;
using PS.MiniEcommerce.Application.Notifications;

namespace PS.MiniEcommerce.IntegrationTests;

public sealed class TestNotificationSender : INotificationSender
{
    private readonly ConcurrentQueue<(string Email, Notification Notification)> _sent = new();

    public IReadOnlyList<(string Email, Notification Notification)> GetAll() => _sent.ToArray();

    public int Count => _sent.Count;

    public bool Contains<T>() =>
        _sent.Any(entry => entry.Notification.EventType == typeof(T).Name);

    public int CountOf<T>() =>
        _sent.Count(entry => entry.Notification.EventType == typeof(T).Name);

    public Task SendAsync(
        string email,
        Notification notification,
        CancellationToken cancellationToken)
    {
        _sent.Enqueue((email, notification));
        return Task.CompletedTask;
    }

    public void Clear()
    {
        while (_sent.TryDequeue(out _))
        {
        }
    }
}

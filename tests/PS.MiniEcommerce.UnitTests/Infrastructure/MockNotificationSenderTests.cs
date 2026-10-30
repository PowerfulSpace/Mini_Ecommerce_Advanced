using Microsoft.Extensions.Logging;
using PS.MiniEcommerce.Application.Notifications;
using PS.MiniEcommerce.Infrastructure.Notifications;

namespace PS.MiniEcommerce.UnitTests.Infrastructure;

public class MockNotificationSenderTests
{
    [Fact]
    public async Task SendAsync_LogsInformationWithRecipientAndNotification()
    {
        var logger = new RecordingLogger<MockNotificationSender>();
        var sender = new MockNotificationSender(logger);
        var notification = new Notification(
            "OrderPlaced",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "Order placed",
            19.99m);

        await sender.SendAsync("user@example.com", notification, CancellationToken.None);

        var message = Assert.Single(logger.Messages);
        Assert.Equal(LogLevel.Information, message.Level);
        Assert.Contains("Notification (mock)", message.Message);
        Assert.Contains("user@example.com", message.Message);
        Assert.Contains(notification.EventType, message.Message);
        Assert.Contains(notification.EntityId.ToString(), message.Message);
    }

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

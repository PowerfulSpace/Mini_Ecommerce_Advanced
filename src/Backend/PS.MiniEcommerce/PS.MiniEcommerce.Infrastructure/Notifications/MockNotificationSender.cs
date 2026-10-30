using Microsoft.Extensions.Logging;
using PS.MiniEcommerce.Application.Notifications;

namespace PS.MiniEcommerce.Infrastructure.Notifications;

public sealed class MockNotificationSender : INotificationSender
{
    private readonly ILogger<MockNotificationSender> _logger;

    public MockNotificationSender(ILogger<MockNotificationSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string email, Notification notification, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Notification (mock): {EventType} {EntityId} -> {Email}",
            notification.EventType,
            notification.EntityId,
            email);

        return Task.CompletedTask;
    }
}

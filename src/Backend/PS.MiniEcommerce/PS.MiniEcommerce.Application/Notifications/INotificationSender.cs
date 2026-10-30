namespace PS.MiniEcommerce.Application.Notifications;

public interface INotificationSender
{
    Task SendAsync(string email, Notification notification, CancellationToken cancellationToken);
}

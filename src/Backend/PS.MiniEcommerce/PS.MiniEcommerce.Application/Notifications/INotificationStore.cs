namespace PS.MiniEcommerce.Application.Notifications;

public interface INotificationStore
{
    bool TryAdd(Notification notification);

    IReadOnlyList<Notification> GetAll();
}

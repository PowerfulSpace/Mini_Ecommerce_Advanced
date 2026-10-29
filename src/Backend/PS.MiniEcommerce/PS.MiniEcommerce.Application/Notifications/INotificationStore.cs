namespace PS.MiniEcommerce.Application.Notifications;

public interface INotificationStore
{
    void Add(Notification notification);

    IReadOnlyList<Notification> GetAll();
}

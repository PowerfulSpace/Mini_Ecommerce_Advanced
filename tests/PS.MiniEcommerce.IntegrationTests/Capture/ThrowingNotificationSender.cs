using PS.MiniEcommerce.Application.Notifications;

namespace PS.MiniEcommerce.IntegrationTests;

public sealed class ThrowingNotificationSender : INotificationSender
{
    private int _failuresRemaining;
    private int _attempts;

    public ThrowingNotificationSender(int failuresBeforeSuccess)
    {
        _failuresRemaining = failuresBeforeSuccess;
    }

    public int Attempts => _attempts;

    public Task SendAsync(
        string email,
        Notification notification,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _attempts);

        if (Interlocked.Decrement(ref _failuresRemaining) >= 0)
        {
            throw new InvalidOperationException("Simulated notification sender failure.");
        }

        return Task.CompletedTask;
    }
}

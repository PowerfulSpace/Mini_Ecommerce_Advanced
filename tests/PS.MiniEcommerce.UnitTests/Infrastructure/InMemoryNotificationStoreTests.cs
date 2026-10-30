using Microsoft.Extensions.Logging.Abstractions;
using PS.MiniEcommerce.Application.Notifications;
using PS.MiniEcommerce.Infrastructure.Notifications;

namespace PS.MiniEcommerce.UnitTests.Infrastructure;

public class InMemoryNotificationStoreTests
{
    private static readonly DateTime OccurredAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TryAdd_WhenNotificationIsNew_StoresNotificationAndReturnsTrue()
    {
        var store = CreateStore();
        var notification = CreateNotification();

        var added = store.TryAdd(notification);

        Assert.True(added);
        var stored = Assert.Single(store.GetAll());
        Assert.Equal(notification, stored);
    }

    [Fact]
    public void TryAdd_WhenNotificationHasSameNaturalKey_ReturnsFalseAndDoesNotDuplicate()
    {
        var store = CreateStore();
        var first = CreateNotification();
        var duplicate = first with { CreatedAtUtc = first.CreatedAtUtc.AddMinutes(1) };

        Assert.True(store.TryAdd(first));
        Assert.False(store.TryAdd(duplicate));

        var stored = Assert.Single(store.GetAll());
        Assert.Equal(first, stored);
    }

    [Fact]
    public void TryAdd_WhenNaturalKeysDiffer_StoresBothNotifications()
    {
        var store = CreateStore();
        var first = CreateNotification();
        var second = CreateNotification(entityId: Guid.NewGuid());

        Assert.True(store.TryAdd(first));
        Assert.True(store.TryAdd(second));

        Assert.Equal(2, store.GetAll().Count);
    }

    [Fact]
    public void GetAll_ReturnsSnapshot()
    {
        var store = CreateStore();
        store.TryAdd(CreateNotification());

        var snapshot = store.GetAll();

        store.TryAdd(CreateNotification(entityId: Guid.NewGuid()));

        Assert.Single(snapshot);
        Assert.Equal(2, store.GetAll().Count);
    }

    private static InMemoryNotificationStore CreateStore() =>
        new(NullLogger<InMemoryNotificationStore>.Instance);

    private static Notification CreateNotification(Guid? entityId = null) =>
        new(
            "OrderPlaced",
            entityId ?? Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            DateTime.UtcNow,
            OccurredAtUtc,
            "Order placed",
            19.99m);
}

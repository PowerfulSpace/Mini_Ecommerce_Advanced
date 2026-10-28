using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Events;
using PS.MiniEcommerce.Application.Events.Handlers;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Events;

namespace PS.MiniEcommerce.UnitTests.Application.Events;

public class AuditHandlerTests
{
    private static readonly DateTime FixedUtcNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid ProductId = Guid.NewGuid();

    [Fact]
    public async Task Handle_OrderPlaced_WritesAuditEntry()
    {
        var orderId = Guid.NewGuid();
        var domainEvent = new OrderPlaced(
            orderId,
            UserId,
            Money.FromDecimal(19.99m),
            2,
            FixedUtcNow);
        var repository = Substitute.For<IAuditLogRepository>();
        AuditLogEntry? captured = null;
        repository
            .AddAsync(Arg.Do<AuditLogEntry>(entry => captured = entry), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var handler = CreateHandler(repository);

        await handler.Handle(
            new DomainEventNotification<object>(domainEvent),
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("OrderPlaced", captured.Action);
        Assert.Equal("Order", captured.EntityType);
        Assert.Equal(orderId, captured.EntityId);
        Assert.Equal(UserId, captured.ActorUserId);
        Assert.Equal(FixedUtcNow, captured.OccurredAtUtc);
        Assert.Null(captured.CorrelationId);

        using var payload = JsonDocument.Parse(captured.Payload!);
        Assert.Equal(19.99m, payload.RootElement.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(2, payload.RootElement.GetProperty("itemCount").GetInt32());
    }

    [Fact]
    public async Task Handle_OrderCancelledPartial_WritesOnlyNewlyCancelledItems()
    {
        var order = CreateOrder(quantity: 3);
        var item = Assert.Single(order.Items);
        order.ClearDomainEvents();
        order.CancelItems(new[] { (item.Id, 1) }, FixedUtcNow);
        var domainEvent = Assert.Single(order.DomainEvents.OfType<OrderCancelled>());

        var repository = Substitute.For<IAuditLogRepository>();
        AuditLogEntry? captured = null;
        repository
            .AddAsync(Arg.Do<AuditLogEntry>(entry => captured = entry), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var handler = CreateHandler(repository);

        await handler.Handle(
            new DomainEventNotification<object>(domainEvent),
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("OrderCancelled", captured.Action);
        Assert.Equal("Order", captured.EntityType);
        Assert.Equal(order.Id, captured.EntityId);
        Assert.Equal(UserId, captured.ActorUserId);
        Assert.Equal(FixedUtcNow, captured.OccurredAtUtc);

        using var payload = JsonDocument.Parse(captured.Payload!);
        Assert.False(payload.RootElement.GetProperty("isFullCancellation").GetBoolean());
        var items = payload.RootElement.GetProperty("items");
        var cancelledItem = Assert.Single(items.EnumerateArray());
        Assert.Equal(item.Id, cancelledItem.GetProperty("orderItemId").GetGuid());
        Assert.Equal(ProductId, cancelledItem.GetProperty("productId").GetGuid());
        Assert.Equal(1, cancelledItem.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Handle_OrderCancelledFull_WritesIsFullCancellationTrue()
    {
        var order = CreateOrder(quantity: 2);
        order.ClearDomainEvents();
        order.Cancel(FixedUtcNow);
        var domainEvent = Assert.Single(order.DomainEvents.OfType<OrderCancelled>());

        var repository = Substitute.For<IAuditLogRepository>();
        AuditLogEntry? captured = null;
        repository
            .AddAsync(Arg.Do<AuditLogEntry>(entry => captured = entry), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var handler = CreateHandler(repository);

        await handler.Handle(
            new DomainEventNotification<object>(domainEvent),
            CancellationToken.None);

        Assert.NotNull(captured);
        using var payload = JsonDocument.Parse(captured.Payload!);
        Assert.True(payload.RootElement.GetProperty("isFullCancellation").GetBoolean());
    }

    [Fact]
    public async Task Handle_ProductDeactivated_WritesAuditEntryForCurrentAdmin()
    {
        var domainEvent = new ProductDeactivated(ProductId, FixedUtcNow);
        var repository = Substitute.For<IAuditLogRepository>();
        AuditLogEntry? captured = null;
        repository
            .AddAsync(Arg.Do<AuditLogEntry>(entry => captured = entry), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(UserId);
        var handler = CreateHandler(repository, currentUser: currentUser);

        await handler.Handle(
            new DomainEventNotification<object>(domainEvent),
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("ProductDeactivated", captured.Action);
        Assert.Equal("Product", captured.EntityType);
        Assert.Equal(ProductId, captured.EntityId);
        Assert.Equal(UserId, captured.ActorUserId);
        Assert.Null(captured.Payload);
        Assert.Equal(FixedUtcNow, captured.OccurredAtUtc);
    }

    [Fact]
    public async Task Handle_StockAdjusted_WritesAuditEntryWithNewAvailable()
    {
        var domainEvent = new StockAdjusted(ProductId, 7, FixedUtcNow);
        var repository = Substitute.For<IAuditLogRepository>();
        AuditLogEntry? captured = null;
        repository
            .AddAsync(Arg.Do<AuditLogEntry>(entry => captured = entry), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(UserId);
        var handler = CreateHandler(repository, currentUser: currentUser);

        await handler.Handle(
            new DomainEventNotification<object>(domainEvent),
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("StockAdjusted", captured.Action);
        Assert.Equal("Product", captured.EntityType);
        Assert.Equal(ProductId, captured.EntityId);
        Assert.Equal(UserId, captured.ActorUserId);
        Assert.Equal(FixedUtcNow, captured.OccurredAtUtc);

        using var payload = JsonDocument.Parse(captured.Payload!);
        Assert.Equal(7, payload.RootElement.GetProperty("newAvailable").GetInt32());
    }

    [Fact]
    public async Task Handle_OrderShipped_WritesAuditEntryForCurrentAdmin()
    {
        var orderId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var domainEvent = new OrderShipped(orderId, ownerId, FixedUtcNow);
        var repository = Substitute.For<IAuditLogRepository>();
        AuditLogEntry? captured = null;
        repository
            .AddAsync(Arg.Do<AuditLogEntry>(entry => captured = entry), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(UserId);
        var handler = CreateHandler(repository, currentUser: currentUser);

        await handler.Handle(
            new DomainEventNotification<object>(domainEvent),
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("OrderShipped", captured.Action);
        Assert.Equal("Order", captured.EntityType);
        Assert.Equal(orderId, captured.EntityId);
        Assert.Equal(UserId, captured.ActorUserId);
        Assert.NotEqual(ownerId, captured.ActorUserId);
        Assert.Null(captured.Payload);
        Assert.Equal(FixedUtcNow, captured.OccurredAtUtc);
    }

    [Fact]
    public async Task Handle_OrderDelivered_WritesAuditEntryForCurrentAdmin()
    {
        var orderId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var domainEvent = new OrderDelivered(orderId, ownerId, FixedUtcNow);
        var repository = Substitute.For<IAuditLogRepository>();
        AuditLogEntry? captured = null;
        repository
            .AddAsync(Arg.Do<AuditLogEntry>(entry => captured = entry), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(UserId);
        var handler = CreateHandler(repository, currentUser: currentUser);

        await handler.Handle(
            new DomainEventNotification<object>(domainEvent),
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("OrderDelivered", captured.Action);
        Assert.Equal("Order", captured.EntityType);
        Assert.Equal(orderId, captured.EntityId);
        Assert.Equal(UserId, captured.ActorUserId);
        Assert.NotEqual(ownerId, captured.ActorUserId);
        Assert.Null(captured.Payload);
        Assert.Equal(FixedUtcNow, captured.OccurredAtUtc);
    }

    [Fact]
    public async Task Handle_OrderReturned_WritesAuditEntryForCurrentAdmin()
    {
        var orderId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var domainEvent = new OrderReturned(orderId, ownerId, FixedUtcNow);
        var repository = Substitute.For<IAuditLogRepository>();
        AuditLogEntry? captured = null;
        repository
            .AddAsync(Arg.Do<AuditLogEntry>(entry => captured = entry), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(UserId);
        var handler = CreateHandler(repository, currentUser: currentUser);

        await handler.Handle(
            new DomainEventNotification<object>(domainEvent),
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("OrderReturned", captured.Action);
        Assert.Equal("Order", captured.EntityType);
        Assert.Equal(orderId, captured.EntityId);
        Assert.Equal(UserId, captured.ActorUserId);
        Assert.NotEqual(ownerId, captured.ActorUserId);
        Assert.Null(captured.Payload);
        Assert.Equal(FixedUtcNow, captured.OccurredAtUtc);
    }

    [Fact]
    public async Task Handle_ProductEventWithoutCurrentUser_WritesNullActor()
    {
        var domainEvent = new ProductDeactivated(ProductId, FixedUtcNow);
        var repository = Substitute.For<IAuditLogRepository>();
        AuditLogEntry? captured = null;
        repository
            .AddAsync(Arg.Do<AuditLogEntry>(entry => captured = entry), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var handler = CreateHandler(repository);

        await handler.Handle(
            new DomainEventNotification<object>(domainEvent),
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Null(captured.ActorUserId);
    }

    [Fact]
    public async Task Handle_UnknownEvent_LogsWarningAndDoesNotWriteAudit()
    {
        var domainEvent = new OrderPaid(Guid.NewGuid(), UserId, FixedUtcNow);
        var repository = Substitute.For<IAuditLogRepository>();
        var logger = new RecordingLogger<AuditHandler>();
        var handler = CreateHandler(repository, logger);

        await handler.Handle(
            new DomainEventNotification<object>(domainEvent),
            CancellationToken.None);

        await repository.DidNotReceive().AddAsync(
            Arg.Any<AuditLogEntry>(),
            Arg.Any<CancellationToken>());
        Assert.Contains(
            logger.Messages,
            message => message.Level == LogLevel.Warning && message.Message.Contains(nameof(OrderPaid)));
    }

    private static AuditHandler CreateHandler(
        IAuditLogRepository repository,
        ILogger<AuditHandler>? logger = null,
        ICurrentUser? currentUser = null)
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IAuditLogRepository)).Returns(repository);

        if (currentUser is not null)
        {
            serviceProvider.GetService(typeof(ICurrentUser)).Returns(currentUser);
        }

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(serviceProvider);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        return new AuditHandler(
            scopeFactory,
            logger ?? new RecordingLogger<AuditHandler>());
    }

    private static Order CreateOrder(int quantity) =>
        Order.Create(
            UserId,
            new[] { new OrderItemData(ProductId, "Widget", Money.FromDecimal(19.99m), quantity) },
            FixedUtcNow);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

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

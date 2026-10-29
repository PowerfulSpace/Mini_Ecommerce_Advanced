using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PS.MiniEcommerce.Application.Abstractions.Outbox;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Infrastructure.Outbox;
using PS.MiniEcommerce.Infrastructure.Serialization;

namespace PS.MiniEcommerce.UnitTests.Infrastructure;

public class OutboxDispatcherTests
{
    private readonly IOutboxRepository _repository = Substitute.For<IOutboxRepository>();

    [Fact]
    public async Task DispatchBatch_WhenMessageValid_MarksProcessedAndDeliversEvent()
    {
        var orderPlaced = CreateOrderPlaced();
        var message = CreateMessage(orderPlaced);
        var subscriber = new RecordingSubscriber();

        _repository
            .GetPendingAsync(20, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new[] { message });

        var dispatcher = CreateDispatcher(subscriber);

        await dispatcher.DispatchBatchAsync(CancellationToken.None);

        var delivered = Assert.IsType<OrderPlaced>(Assert.Single(subscriber.Events));
        Assert.Equal(orderPlaced.OrderId, delivered.OrderId);
        Assert.Equal(19.99m, delivered.TotalAmount.Amount);

        await _repository.Received(1).MarkProcessedAsync(
            message.Id,
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().MarkFailedAttemptAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        await _repository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchBatch_WhenEventTypeUnknown_MarksFailedAttempt()
    {
        var message = new OutboxMessageData(
            Guid.NewGuid(),
            "Unknown.EventType",
            "{}",
            DateTime.UtcNow,
            0);

        _repository
            .GetPendingAsync(20, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new[] { message });

        var subscriber = new RecordingSubscriber();
        var dispatcher = CreateDispatcher(subscriber);

        await dispatcher.DispatchBatchAsync(CancellationToken.None);

        Assert.Empty(subscriber.Events);
        await _repository.Received(1).MarkFailedAttemptAsync(
            message.Id,
            Arg.Is<string>(error => error.Contains("Unknown event type")),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().MarkProcessedAsync(
            Arg.Any<Guid>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchBatch_WhenPayloadInvalid_MarksFailedAttempt()
    {
        var message = new OutboxMessageData(
            Guid.NewGuid(),
            typeof(OrderPlaced).FullName!,
            "{not-json",
            DateTime.UtcNow,
            0);

        _repository
            .GetPendingAsync(20, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new[] { message });

        var subscriber = new RecordingSubscriber();
        var dispatcher = CreateDispatcher(subscriber);

        await dispatcher.DispatchBatchAsync(CancellationToken.None);

        Assert.Empty(subscriber.Events);
        await _repository.Received(1).MarkFailedAttemptAsync(
            message.Id,
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchBatch_WhenSubscriberThrows_MarksFailedAttempt()
    {
        var message = CreateMessage(CreateOrderPlaced());

        _repository
            .GetPendingAsync(20, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new[] { message });

        var dispatcher = CreateDispatcher(new ThrowingSubscriber());

        await dispatcher.DispatchBatchAsync(CancellationToken.None);

        await _repository.Received(1).MarkFailedAttemptAsync(
            message.Id,
            Arg.Is<string>(error => error.Contains("Simulated subscriber failure")),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().MarkProcessedAsync(
            Arg.Any<Guid>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchBatch_WhenNoMessages_DoesNotSave()
    {
        _repository
            .GetPendingAsync(20, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<OutboxMessageData>());

        var dispatcher = CreateDispatcher(new RecordingSubscriber());

        await dispatcher.DispatchBatchAsync(CancellationToken.None);

        await _repository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private OutboxDispatcher CreateDispatcher(params IOutboxSubscriber[] subscribers)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_repository);

        foreach (var subscriber in subscribers)
        {
            services.AddSingleton(subscriber);
        }

        var provider = services.BuildServiceProvider();

        return new OutboxDispatcher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<OutboxDispatcher>.Instance);
    }

    private static OutboxMessageData CreateMessage(OrderPlaced domainEvent)
    {
        var payload = JsonSerializer.Serialize(
            domainEvent,
            domainEvent.GetType(),
            OutboxJsonOptions.Instance);

        return new OutboxMessageData(
            Guid.NewGuid(),
            typeof(OrderPlaced).FullName!,
            payload,
            DateTime.UtcNow,
            0);
    }

    private static OrderPlaced CreateOrderPlaced() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Money.FromDecimal(19.99m),
            1,
            DateTime.UtcNow);

    private sealed class RecordingSubscriber : IOutboxSubscriber
    {
        private readonly List<object> _events = new();

        public IReadOnlyList<object> Events => _events;

        public Task HandleAsync(object domainEvent, CancellationToken cancellationToken)
        {
            _events.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSubscriber : IOutboxSubscriber
    {
        public Task HandleAsync(object domainEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated subscriber failure.");
    }
}

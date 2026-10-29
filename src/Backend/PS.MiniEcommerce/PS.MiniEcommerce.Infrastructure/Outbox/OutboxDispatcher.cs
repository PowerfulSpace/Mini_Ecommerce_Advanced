using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PS.MiniEcommerce.Application.Abstractions.Outbox;
using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Infrastructure.Serialization;

namespace PS.MiniEcommerce.Infrastructure.Outbox;

public class OutboxDispatcher : BackgroundService
{
    private const int BatchSize = 20;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to dispatch outbox messages.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    internal async Task DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var subscribers = scope.ServiceProvider.GetServices<IOutboxSubscriber>().ToList();

        var pending = await repository.GetPendingAsync(
            BatchSize,
            DateTime.UtcNow,
            cancellationToken);

        foreach (var message in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var eventType = ResolveEventType(message.EventType);
            if (eventType is null)
            {
                await repository.MarkFailedAttemptAsync(
                    message.Id,
                    $"Unknown event type '{message.EventType}'.",
                    DateTime.UtcNow,
                    cancellationToken);
                continue;
            }

            object? domainEvent;

            try
            {
                domainEvent = JsonSerializer.Deserialize(
                    message.Payload,
                    eventType,
                    OutboxJsonOptions.Instance);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                await repository.MarkFailedAttemptAsync(
                    message.Id,
                    Describe(exception),
                    DateTime.UtcNow,
                    cancellationToken);
                continue;
            }

            if (domainEvent is null)
            {
                await repository.MarkFailedAttemptAsync(
                    message.Id,
                    "Outbox payload deserialized to null.",
                    DateTime.UtcNow,
                    cancellationToken);
                continue;
            }

            try
            {
                foreach (var subscriber in subscribers)
                {
                    await subscriber.HandleAsync(domainEvent, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Outbox subscriber failed for message {MessageId} of type {EventType}.",
                    message.Id,
                    message.EventType);

                await repository.MarkFailedAttemptAsync(
                    message.Id,
                    Describe(exception),
                    DateTime.UtcNow,
                    cancellationToken);
                continue;
            }

            await repository.MarkProcessedAsync(message.Id, DateTime.UtcNow, cancellationToken);
        }

        if (pending.Count > 0)
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
    }

    private static Type? ResolveEventType(string eventType) =>
        typeof(OrderPlaced).Assembly.GetType(eventType, throwOnError: false);

    private static string Describe(Exception exception) =>
        $"{exception.GetType().Name}: {exception.Message}";
}

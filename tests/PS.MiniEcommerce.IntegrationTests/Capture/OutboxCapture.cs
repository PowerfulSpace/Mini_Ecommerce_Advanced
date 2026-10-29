using System.Collections.Concurrent;
using PS.MiniEcommerce.Application.Abstractions.Outbox;

namespace PS.MiniEcommerce.IntegrationTests;

public sealed class OutboxCapture
{
    private readonly ConcurrentQueue<object> _events = new();

    public IReadOnlyList<object> Events => _events.ToArray();

    public int Count => _events.Count;

    public void Add(object domainEvent)
    {
        _events.Enqueue(domainEvent);
    }

    public bool Contains<T>() => _events.OfType<T>().Any();

    public int CountOf<T>() => _events.OfType<T>().Count();

    public void Clear()
    {
        while (_events.TryDequeue(out _))
        {
        }
    }
}

public sealed class TestOutboxSubscriber : IOutboxSubscriber
{
    private readonly OutboxCapture _capture;

    public TestOutboxSubscriber(OutboxCapture capture)
    {
        _capture = capture;
    }

    public Task HandleAsync(object domainEvent, CancellationToken cancellationToken)
    {
        _capture.Add(domainEvent);
        return Task.CompletedTask;
    }
}

public sealed class ThrowingOutboxSubscriber : IOutboxSubscriber
{
    private int _failuresRemaining;
    private int _deliveryAttempts;

    public ThrowingOutboxSubscriber(int failuresBeforeSuccess)
    {
        _failuresRemaining = failuresBeforeSuccess;
    }

    public int DeliveryAttempts => _deliveryAttempts;

    public Task HandleAsync(object domainEvent, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _deliveryAttempts);

        if (Interlocked.Decrement(ref _failuresRemaining) >= 0)
        {
            throw new InvalidOperationException("Simulated outbox subscriber failure.");
        }

        return Task.CompletedTask;
    }
}

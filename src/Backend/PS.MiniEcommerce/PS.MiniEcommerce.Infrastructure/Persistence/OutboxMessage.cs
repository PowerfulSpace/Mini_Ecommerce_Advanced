namespace PS.MiniEcommerce.Infrastructure.Persistence;

public class OutboxMessage
{
    public Guid Id { get; private set; }

    public string EventType { get; private set; }

    public string Payload { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime? ProcessedAtUtc { get; private set; }

    public DateTime? LastAttemptAtUtc { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    private OutboxMessage()
    {
        EventType = string.Empty;
        Payload = string.Empty;
    }

    public static OutboxMessage Create(string eventType, string payload, DateTime utcNow)
    {
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            Payload = payload,
            CreatedAtUtc = utcNow,
            ProcessedAtUtc = null,
            LastAttemptAtUtc = null,
            Attempts = 0,
            LastError = null
        };
    }

    public void MarkProcessed(DateTime utcNow)
    {
        ProcessedAtUtc = utcNow;
    }

    public void MarkFailedAttempt(string error, DateTime utcNow, int maxAttempts)
    {
        Attempts++;
        LastAttemptAtUtc = utcNow;
        LastError = error.Length > 2000 ? error[..2000] : error;

        if (Attempts >= maxAttempts)
        {
            ProcessedAtUtc = utcNow;
        }
    }
}

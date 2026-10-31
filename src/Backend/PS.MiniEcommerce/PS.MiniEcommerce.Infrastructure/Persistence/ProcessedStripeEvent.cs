namespace PS.MiniEcommerce.Infrastructure.Persistence;

public class ProcessedStripeEvent
{
    public Guid Id { get; private set; }

    public string EventId { get; private set; }

    public string Type { get; private set; }

    public DateTime ProcessedAtUtc { get; private set; }

    private ProcessedStripeEvent()
    {
        EventId = string.Empty;
        Type = string.Empty;
    }

    public static ProcessedStripeEvent Create(string eventId, string type, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            throw new ArgumentException("Stripe event id must not be empty.", nameof(eventId));
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("Stripe event type must not be empty.", nameof(type));
        }

        return new ProcessedStripeEvent
        {
            Id = Guid.NewGuid(),
            EventId = eventId,
            Type = type,
            ProcessedAtUtc = utcNow
        };
    }
}

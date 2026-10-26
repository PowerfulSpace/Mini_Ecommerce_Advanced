namespace PS.MiniEcommerce.Infrastructure.Persistence;

public class CheckoutAttempt
{
    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string IdempotencyKey { get; private set; }

    public byte[] RequestFingerprint { get; private set; }

    public CheckoutAttemptStatus Status { get; private set; }

    public Guid? OrderId { get; private set; }

    public int? ResponseStatusCode { get; private set; }

    public string? ResponsePayload { get; private set; }

    public DateTime LeaseExpiresAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    private CheckoutAttempt()
    {
        IdempotencyKey = string.Empty;
        RequestFingerprint = Array.Empty<byte>();
    }

    public static CheckoutAttempt Create(
        Guid userId,
        string idempotencyKey,
        byte[] requestFingerprint,
        DateTime utcNow,
        TimeSpan leaseDuration)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ArgumentException("Idempotency key must not be empty.", nameof(idempotencyKey));
        }

        if (requestFingerprint is null || requestFingerprint.Length != 32)
        {
            throw new ArgumentException(
                "Request fingerprint must be a 32-byte SHA-256 hash.",
                nameof(requestFingerprint));
        }

        return new CheckoutAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = requestFingerprint,
            Status = CheckoutAttemptStatus.InProgress,
            OrderId = null,
            ResponseStatusCode = null,
            ResponsePayload = null,
            LeaseExpiresAtUtc = utcNow + leaseDuration,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow
        };
    }

    public void MarkCompleted(Guid orderId, int statusCode, string responsePayload, DateTime utcNow)
    {
        if (Status == CheckoutAttemptStatus.Completed)
        {
            throw new InvalidOperationException("Checkout attempt is already completed.");
        }

        OrderId = orderId;
        ResponseStatusCode = statusCode;
        ResponsePayload = responsePayload;
        Status = CheckoutAttemptStatus.Completed;
        UpdatedAtUtc = utcNow;
    }

    public void MarkCompletedWithError(int statusCode, string responsePayload, DateTime utcNow)
    {
        if (Status == CheckoutAttemptStatus.Completed)
        {
            throw new InvalidOperationException("Checkout attempt is already completed.");
        }

        ResponseStatusCode = statusCode;
        ResponsePayload = responsePayload;
        Status = CheckoutAttemptStatus.Completed;
        UpdatedAtUtc = utcNow;
    }

    public void ExtendLease(DateTime utcNow, TimeSpan leaseDuration)
    {
        LeaseExpiresAtUtc = utcNow + leaseDuration;
        UpdatedAtUtc = utcNow;
    }
}

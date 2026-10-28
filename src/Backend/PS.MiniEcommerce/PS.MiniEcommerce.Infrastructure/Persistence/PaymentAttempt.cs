namespace PS.MiniEcommerce.Infrastructure.Persistence;

public class PaymentAttempt
{
    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string IdempotencyKey { get; private set; }

    public byte[] RequestFingerprint { get; private set; }

    public PaymentAttemptStatus Status { get; private set; }

    public Guid? PaymentId { get; private set; }

    public int? ResponseStatusCode { get; private set; }

    public string? ResponsePayload { get; private set; }

    public DateTime LeaseExpiresAtUtc { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    private PaymentAttempt()
    {
        IdempotencyKey = string.Empty;
        RequestFingerprint = Array.Empty<byte>();
    }

    public static PaymentAttempt Create(
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

        return new PaymentAttempt
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = requestFingerprint,
            Status = PaymentAttemptStatus.InProgress,
            PaymentId = null,
            ResponseStatusCode = null,
            ResponsePayload = null,
            LeaseExpiresAtUtc = utcNow + leaseDuration,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow
        };
    }

    public void MarkCompleted(Guid paymentId, int statusCode, string responsePayload, DateTime utcNow)
    {
        PaymentId = paymentId;
        ResponseStatusCode = statusCode;
        ResponsePayload = responsePayload;
        Status = PaymentAttemptStatus.Completed;
        UpdatedAtUtc = utcNow;
    }

    public void MarkCompletedWithError(int statusCode, string responsePayload, DateTime utcNow)
    {
        ResponseStatusCode = statusCode;
        ResponsePayload = responsePayload;
        Status = PaymentAttemptStatus.Completed;
        UpdatedAtUtc = utcNow;
    }

    public void ExtendLease(DateTime utcNow, TimeSpan leaseDuration)
    {
        LeaseExpiresAtUtc = utcNow + leaseDuration;
        UpdatedAtUtc = utcNow;
    }
}

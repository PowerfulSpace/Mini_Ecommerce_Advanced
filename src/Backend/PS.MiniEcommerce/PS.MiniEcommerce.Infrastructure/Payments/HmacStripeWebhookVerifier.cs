using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PS.MiniEcommerce.Application.Abstractions.Payments;

namespace PS.MiniEcommerce.Infrastructure.Payments;

public sealed class HmacStripeWebhookVerifier : IStripeWebhookVerifier
{
    private readonly StripeOptions _options;
    private readonly ILogger<HmacStripeWebhookVerifier> _logger;

    public HmacStripeWebhookVerifier(
        IOptions<StripeOptions> options,
        ILogger<HmacStripeWebhookVerifier> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public bool TryVerify(string payload, string signatureHeader)
    {
        if (string.IsNullOrEmpty(payload)
            || string.IsNullOrWhiteSpace(signatureHeader))
        {
            return Fail("missing payload or signature header");
        }

        if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
        {
            return Fail("webhook secret is not configured");
        }

        long? timestamp = null;
        var signatures = new List<string>();

        foreach (var item in signatureHeader.Split(
                     ',',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = item.Split('=', 2);

            if (parts.Length != 2)
            {
                continue;
            }

            if (parts[0] == "t")
            {
                if (long.TryParse(
                        parts[1],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var parsedTimestamp))
                {
                    timestamp = parsedTimestamp;
                }
            }
            else if (parts[0] == "v1" && !string.IsNullOrWhiteSpace(parts[1]))
            {
                signatures.Add(parts[1]);
            }
        }

        if (timestamp is null)
        {
            return Fail("missing timestamp");
        }

        if (signatures.Count == 0)
        {
            return Fail("missing signature");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var tolerance = _options.ToleranceSeconds;

        if (tolerance < 0
            || timestamp.Value < now - tolerance
            || timestamp.Value > now + tolerance)
        {
            return Fail("timestamp is outside the allowed tolerance");
        }

        var expectedSignature = ComputeSignature(timestamp.Value, payload, _options.WebhookSecret);

        foreach (var signature in signatures)
        {
            byte[] providedSignature;

            try
            {
                providedSignature = Convert.FromHexString(signature);
            }
            catch (FormatException)
            {
                continue;
            }

            if (providedSignature.Length == expectedSignature.Length
                && CryptographicOperations.FixedTimeEquals(providedSignature, expectedSignature))
            {
                return true;
            }
        }

        return Fail("signature does not match");
    }

    private static byte[] ComputeSignature(long timestamp, string payload, string secret)
    {
        var signedPayload = $"{timestamp.ToString(CultureInfo.InvariantCulture)}.{payload}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));

        return hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
    }

    private bool Fail(string reason)
    {
        _logger.LogWarning("Stripe webhook signature verification failed: {Reason}.", reason);
        return false;
    }
}

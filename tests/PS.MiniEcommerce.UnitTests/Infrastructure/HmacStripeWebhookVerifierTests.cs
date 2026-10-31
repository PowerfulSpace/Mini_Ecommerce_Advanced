using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PS.MiniEcommerce.Infrastructure.Payments;

namespace PS.MiniEcommerce.UnitTests.Infrastructure;

public class HmacStripeWebhookVerifierTests
{
    private const string Secret = "whsec_test_secret_123456";
    private const string Payload = "{\"id\":\"evt_123\",\"type\":\"payment_intent.succeeded\"}";

    [Fact]
    public void TryVerify_WithValidSignature_ReturnsTrue()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = ComputeSignature(timestamp, Payload, Secret);
        var verifier = CreateVerifier();

        var result = verifier.TryVerify(Payload, $"t={timestamp},v1={signature}");

        Assert.True(result);
    }

    [Fact]
    public void TryVerify_WithInvalidSignature_ReturnsFalse()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = ComputeSignature(timestamp, Payload, "wrong_secret_123456");
        var verifier = CreateVerifier();

        var result = verifier.TryVerify(Payload, $"t={timestamp},v1={signature}");

        Assert.False(result);
    }

    [Fact]
    public void TryVerify_WithExpiredTimestamp_ReturnsFalse()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 301;
        var signature = ComputeSignature(timestamp, Payload, Secret);
        var verifier = CreateVerifier();

        var result = verifier.TryVerify(Payload, $"t={timestamp},v1={signature}");

        Assert.False(result);
    }

    [Fact]
    public void TryVerify_WithoutTimestamp_ReturnsFalse()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = ComputeSignature(timestamp, Payload, Secret);
        var verifier = CreateVerifier();

        var result = verifier.TryVerify(Payload, $"v1={signature}");

        Assert.False(result);
    }

    [Fact]
    public void TryVerify_WithoutSignature_ReturnsFalse()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var verifier = CreateVerifier();

        var result = verifier.TryVerify(Payload, $"t={timestamp}");

        Assert.False(result);
    }

    [Fact]
    public void TryVerify_WithNonHexSignature_ReturnsFalse()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var verifier = CreateVerifier();

        var result = verifier.TryVerify(Payload, $"t={timestamp},v1=not-hex");

        Assert.False(result);
    }

    [Fact]
    public void TryVerify_WithAnyMatchingSignature_ReturnsTrue()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = ComputeSignature(timestamp, Payload, Secret);
        var verifier = CreateVerifier();

        var result = verifier.TryVerify(
            Payload,
            $"t={timestamp},v1=deadbeef,v1={signature}");

        Assert.True(result);
    }

    [Fact]
    public void TryVerify_WithEmptySecret_ReturnsFalse()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var verifier = CreateVerifier(webhookSecret: string.Empty);

        var result = verifier.TryVerify(
            Payload,
            $"t={timestamp},v1={ComputeSignature(timestamp, Payload, Secret)}");

        Assert.False(result);
    }

    private static HmacStripeWebhookVerifier CreateVerifier(
        string webhookSecret = Secret,
        int toleranceSeconds = 300)
    {
        var options = Options.Create(new StripeOptions
        {
            Enabled = true,
            WebhookSecret = webhookSecret,
            ToleranceSeconds = toleranceSeconds
        });

        return new HmacStripeWebhookVerifier(
            options,
            NullLogger<HmacStripeWebhookVerifier>.Instance);
    }

    private static string ComputeSignature(long timestamp, string payload, string secret)
    {
        var signedPayload = $"{timestamp.ToString(CultureInfo.InvariantCulture)}.{payload}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));

        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload)))
            .ToLowerInvariant();
    }
}

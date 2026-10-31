using PS.MiniEcommerce.Infrastructure.Payments;

namespace PS.MiniEcommerce.UnitTests.Infrastructure;

public class StripeOptionsValidationTests
{
    private readonly StripeOptionsValidator _validator = new();

    [Fact]
    public void Validate_WhenDisabled_DoesNotRequireStripeSettings()
    {
        var options = new StripeOptions { Enabled = false };

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenEnabledWithoutWebhookSecret_Fails()
    {
        var options = CreateEnabledOptions();
        options.WebhookSecret = string.Empty;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Stripe:WebhookSecret"));
    }

    [Fact]
    public void Validate_WhenEnabledWithShortWebhookSecret_Fails()
    {
        var options = CreateEnabledOptions();
        options.WebhookSecret = "short";

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Stripe:WebhookSecret"));
    }

    [Fact]
    public void Validate_WhenEnabledWithoutProviderName_Fails()
    {
        var options = CreateEnabledOptions();
        options.ProviderName = string.Empty;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Stripe:ProviderName"));
    }

    [Fact]
    public void Validate_WhenEnabledWithRequiredSettings_Succeeds()
    {
        var options = CreateEnabledOptions();

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    private static StripeOptions CreateEnabledOptions() =>
        new()
        {
            Enabled = true,
            WebhookSecret = "whsec_test_secret_123456",
            ProviderName = "stripe",
            ToleranceSeconds = 300
        };
}

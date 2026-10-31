using Microsoft.Extensions.Options;

namespace PS.MiniEcommerce.Infrastructure.Payments;

public sealed class StripeOptions
{
    public bool Enabled { get; set; }

    public string WebhookSecret { get; set; } = string.Empty;

    public string ProviderName { get; set; } = "stripe";

    public int ToleranceSeconds { get; set; } = 300;
}

public sealed class StripeOptionsValidator : IValidateOptions<StripeOptions>
{
    private const int MinimumWebhookSecretLength = 16;

    public ValidateOptionsResult Validate(string? name, StripeOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.WebhookSecret)
            || options.WebhookSecret.Length < MinimumWebhookSecretLength)
        {
            failures.Add(
                $"Stripe:WebhookSecret must be at least {MinimumWebhookSecretLength} characters when Stripe:Enabled is true.");
        }

        if (string.IsNullOrWhiteSpace(options.ProviderName))
        {
            failures.Add("Stripe:ProviderName is required when Stripe:Enabled is true.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

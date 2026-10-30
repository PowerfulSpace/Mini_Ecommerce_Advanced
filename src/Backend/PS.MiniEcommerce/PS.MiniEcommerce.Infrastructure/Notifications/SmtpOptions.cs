using Microsoft.Extensions.Options;

namespace PS.MiniEcommerce.Infrastructure.Notifications;

public sealed class SmtpOptions
{
    public bool Enabled { get; set; }

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool UseSsl { get; set; } = true;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = string.Empty;
}

public sealed class SmtpOptionsValidator : IValidateOptions<SmtpOptions>
{
    public ValidateOptionsResult Validate(string? name, SmtpOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            failures.Add("Smtp:Host is required when Smtp:Enabled is true.");
        }

        if (options.Port is <= 0 or > 65535)
        {
            failures.Add("Smtp:Port must be between 1 and 65535 when Smtp:Enabled is true.");
        }

        if (string.IsNullOrWhiteSpace(options.FromAddress))
        {
            failures.Add("Smtp:FromAddress is required when Smtp:Enabled is true.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

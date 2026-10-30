using PS.MiniEcommerce.Infrastructure.Notifications;

namespace PS.MiniEcommerce.UnitTests.Infrastructure;

public class SmtpOptionsValidationTests
{
    private readonly SmtpOptionsValidator _validator = new();

    [Fact]
    public void Validate_WhenDisabled_DoesNotRequireSmtpSettings()
    {
        var options = new SmtpOptions { Enabled = false };

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenEnabledWithRequiredSettings_Succeeds()
    {
        var options = CreateEnabledOptions();

        var result = _validator.Validate(null, options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_WhenEnabledWithoutHost_Fails()
    {
        var options = CreateEnabledOptions();
        options.Host = string.Empty;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Smtp:Host"));
    }

    [Fact]
    public void Validate_WhenEnabledWithInvalidPort_Fails()
    {
        var options = CreateEnabledOptions();
        options.Port = 0;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Smtp:Port"));
    }

    [Fact]
    public void Validate_WhenEnabledWithoutFromAddress_Fails()
    {
        var options = CreateEnabledOptions();
        options.FromAddress = string.Empty;

        var result = _validator.Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures, failure => failure.Contains("Smtp:FromAddress"));
    }

    private static SmtpOptions CreateEnabledOptions() =>
        new()
        {
            Enabled = true,
            Host = "smtp.example.com",
            Port = 587,
            UseSsl = true,
            UserName = "user",
            Password = "password",
            FromAddress = "noreply@example.com",
            FromName = "Mini E-commerce"
        };
}

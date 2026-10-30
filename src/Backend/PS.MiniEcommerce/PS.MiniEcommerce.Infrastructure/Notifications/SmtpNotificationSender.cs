using System.Globalization;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PS.MiniEcommerce.Application.Notifications;

namespace PS.MiniEcommerce.Infrastructure.Notifications;

public sealed class SmtpNotificationSender : INotificationSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpNotificationSender> _logger;

    public SmtpNotificationSender(
        IOptions<SmtpOptions> options,
        ILogger<SmtpNotificationSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string email, Notification notification, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.UseSsl,
            Credentials = new NetworkCredential(_options.UserName, _options.Password)
        };

        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = BuildSubject(notification),
            Body = BuildBody(notification),
            IsBodyHtml = false
        };

        message.To.Add(email);

        await client.SendMailAsync(message, cancellationToken);

        _logger.LogInformation(
            "Notification sent via SMTP: {EventType} {EntityId} -> {Email}",
            notification.EventType,
            notification.EntityId,
            email);
    }

    private static string BuildSubject(Notification notification)
    {
        var referenceId = notification.EventType.StartsWith("Payment", StringComparison.Ordinal)
            ? notification.EntityId
            : notification.OrderId ?? notification.EntityId;

        return $"{notification.EventType} {referenceId}";
    }

    private static string BuildBody(Notification notification) =>
        notification.Amount is null
            ? notification.Message
            : $"{notification.Message}{Environment.NewLine}Amount: {notification.Amount.Value.ToString(CultureInfo.InvariantCulture)}";
}

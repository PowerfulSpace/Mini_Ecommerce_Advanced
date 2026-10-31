using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using PS.MiniEcommerce.Application.Abstractions.Payments;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Events;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Payments;

namespace PS.MiniEcommerce.Application.Webhooks;

public sealed class WebhookHandler : IWebhookHandler
{
    private const string PaymentIntentSucceededType = "payment_intent.succeeded";
    private const string ChargeRefundedType = "charge.refunded";
    private const string RefundUpdatedType = "refund.updated";
    private const string UsdCurrency = "usd";

    private static readonly WebhookResult Ok = new(200);
    private static readonly WebhookResult BadRequest = new(400);

    private readonly IStripeFeature _stripeFeature;
    private readonly IStripeWebhookVerifier _verifier;
    private readonly IProcessedStripeEventRepository _processedEventRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IWebhookTransactionExecutor _transactionExecutor;
    private readonly IMediator _mediator;
    private readonly ILogger<WebhookHandler> _logger;

    public WebhookHandler(
        IStripeFeature stripeFeature,
        IStripeWebhookVerifier verifier,
        IProcessedStripeEventRepository processedEventRepository,
        IPaymentRepository paymentRepository,
        IOrderRepository orderRepository,
        IWebhookTransactionExecutor transactionExecutor,
        IMediator mediator,
        ILogger<WebhookHandler> logger)
    {
        _stripeFeature = stripeFeature;
        _verifier = verifier;
        _processedEventRepository = processedEventRepository;
        _paymentRepository = paymentRepository;
        _orderRepository = orderRepository;
        _transactionExecutor = transactionExecutor;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<WebhookResult> HandleAsync(
        string payload,
        string signatureHeader,
        CancellationToken cancellationToken)
    {
        if (!_stripeFeature.Enabled)
        {
            _logger.LogInformation(
                "Stripe webhooks are disabled; acknowledging the request without processing.");
            return Ok;
        }

        if (!_verifier.TryVerify(payload, signatureHeader))
        {
            _logger.LogWarning("Rejected Stripe webhook with an invalid signature.");
            return BadRequest;
        }

        ParsedEvent? stripeEvent;

        try
        {
            stripeEvent = TryParse(payload);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Rejected Stripe webhook with an invalid JSON payload.");
            return BadRequest;
        }

        if (stripeEvent is null)
        {
            _logger.LogWarning("Rejected Stripe webhook without an event id or type.");
            return BadRequest;
        }

        if (await _processedEventRepository.ExistsAsync(stripeEvent.Id, cancellationToken))
        {
            _logger.LogInformation(
                "Stripe event {EventId} has already been processed.",
                stripeEvent.Id);
            return Ok;
        }

        return stripeEvent.Type switch
        {
            PaymentIntentSucceededType => await HandlePaymentIntentSucceededAsync(
                stripeEvent,
                cancellationToken),
            ChargeRefundedType or RefundUpdatedType => await HandleRefundEventAsync(
                stripeEvent,
                cancellationToken),
            _ => await HandleUnsupportedEventAsync(stripeEvent, cancellationToken)
        };
    }

    private async Task<WebhookResult> HandlePaymentIntentSucceededAsync(
        ParsedEvent stripeEvent,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(stripeEvent.ObjectId))
        {
            return await MarkProcessedAsync(
                stripeEvent,
                "payment intent id is missing",
                utcNow,
                cancellationToken);
        }

        var payment = await _paymentRepository.GetByProviderPaymentIdAsync(
            stripeEvent.ObjectId,
            cancellationToken);

        if (payment is null)
        {
            return await MarkProcessedAsync(
                stripeEvent,
                $"payment with provider payment id '{stripeEvent.ObjectId}' was not found",
                utcNow,
                cancellationToken);
        }

        if (payment.Status == PaymentStatus.Confirmed)
        {
            return await MarkProcessedAsync(
                stripeEvent,
                "payment is already confirmed",
                utcNow,
                cancellationToken);
        }

        if (payment.Status is PaymentStatus.Failed or PaymentStatus.Refunded)
        {
            return await MarkProcessedAsync(
                stripeEvent,
                $"payment is in status {payment.Status}",
                utcNow,
                cancellationToken);
        }

        if (!TryBuildAmount(stripeEvent, out var amount))
        {
            return await MarkProcessedAsync(
                stripeEvent,
                "payment amount is missing or invalid",
                utcNow,
                cancellationToken);
        }

        if (!string.Equals(stripeEvent.Currency, UsdCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return await MarkProcessedAsync(
                stripeEvent,
                $"currency '{stripeEvent.Currency}' is not supported",
                utcNow,
                cancellationToken);
        }

        if (payment.Amount != amount)
        {
            return await MarkProcessedAsync(
                stripeEvent,
                $"amount {amount.Amount} does not match payment amount {payment.Amount.Amount}",
                utcNow,
                cancellationToken);
        }

        var orderStatus = await _orderRepository.GetStatusAsync(payment.OrderId, cancellationToken);

        if (orderStatus != OrderStatus.Created)
        {
            return await MarkProcessedAsync(
                stripeEvent,
                orderStatus is null
                    ? "order was not found"
                    : $"order is in status {orderStatus}",
                utcNow,
                cancellationToken);
        }

        var result = await _transactionExecutor.ExecutePaymentIntentSucceededAsync(
            stripeEvent.Id,
            stripeEvent.Type,
            payment.Id,
            amount,
            utcNow,
            cancellationToken);

        if (result.Processed)
        {
            await PublishDomainEventsAsync(result.DomainEvents, cancellationToken);
        }

        return Ok;
    }

    private async Task<WebhookResult> HandleRefundEventAsync(
        ParsedEvent stripeEvent,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Stripe event {EventId} ({EventType}) is acknowledged without changing payment state.",
            stripeEvent.Id,
            stripeEvent.Type);

        await _processedEventRepository.TryAddAsync(
            stripeEvent.Id,
            stripeEvent.Type,
            DateTime.UtcNow,
            cancellationToken);

        return Ok;
    }

    private async Task<WebhookResult> HandleUnsupportedEventAsync(
        ParsedEvent stripeEvent,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Stripe event {EventId} has unsupported type {EventType}; it is acknowledged without processing.",
            stripeEvent.Id,
            stripeEvent.Type);

        await _processedEventRepository.TryAddAsync(
            stripeEvent.Id,
            stripeEvent.Type,
            DateTime.UtcNow,
            cancellationToken);

        return Ok;
    }

    private async Task<WebhookResult> MarkProcessedAsync(
        ParsedEvent stripeEvent,
        string reason,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Skipping Stripe event {EventId} ({EventType}): {Reason}.",
            stripeEvent.Id,
            stripeEvent.Type,
            reason);

        await _processedEventRepository.TryAddAsync(
            stripeEvent.Id,
            stripeEvent.Type,
            utcNow,
            cancellationToken);

        return Ok;
    }

    private async Task PublishDomainEventsAsync(
        IReadOnlyList<object> domainEvents,
        CancellationToken cancellationToken)
    {
        foreach (var domainEvent in domainEvents)
        {
            try
            {
                await _mediator.Publish(
                    new DomainEventNotification<object>(domainEvent),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to publish domain event {DomainEventType}.",
                    domainEvent.GetType().Name);
            }
        }
    }

    private static bool TryBuildAmount(ParsedEvent stripeEvent, out Money amount)
    {
        if (stripeEvent.AmountInCents is null)
        {
            amount = Money.Zero;
            return false;
        }

        amount = Money.FromDecimal(stripeEvent.AmountInCents.Value / 100m);
        return true;
    }

    private static ParsedEvent? TryParse(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object
            || !TryGetString(root, "id", out var id)
            || !TryGetString(root, "type", out var type))
        {
            return null;
        }

        string? objectId = null;
        string? paymentIntentId = null;
        string? currency = null;
        long? amountInCents = null;

        if (root.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("object", out var dataObject)
            && dataObject.ValueKind == JsonValueKind.Object)
        {
            TryGetString(dataObject, "id", out objectId);
            TryGetString(dataObject, "payment_intent", out paymentIntentId);
            TryGetString(dataObject, "currency", out currency);

            if (dataObject.TryGetProperty("amount", out var amount)
                && amount.ValueKind == JsonValueKind.Number
                && amount.TryGetInt64(out var parsedAmount))
            {
                amountInCents = parsedAmount;
            }
        }

        return new ParsedEvent(id!, type!, objectId, paymentIntentId, currency, amountInCents);
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string? value)
    {
        value = null;

        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private sealed record ParsedEvent(
        string Id,
        string Type,
        string? ObjectId,
        string? PaymentIntentId,
        string? Currency,
        long? AmountInCents);
}

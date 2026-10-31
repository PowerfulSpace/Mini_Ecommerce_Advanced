using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PS.MiniEcommerce.Application.Abstractions.Payments;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Events;
using PS.MiniEcommerce.Application.Webhooks;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Domain.Payments;

namespace PS.MiniEcommerce.UnitTests.Application.Webhooks;

public class WebhookHandlerTests
{
    private const string ProviderPaymentId = "pi_mock_123";
    private const string Signature = "t=1700000000,v1=abcdef";

    private static readonly Guid OrderId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTime FixedUtcNow = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task HandleAsync_WhenStripeIsDisabled_Returns200WithoutVerifyingOrProcessing()
    {
        var stripeFeature = Substitute.For<IStripeFeature>();
        stripeFeature.Enabled.Returns(false);
        var verifier = Substitute.For<IStripeWebhookVerifier>();
        var processedEvents = Substitute.For<IProcessedStripeEventRepository>();
        var handler = CreateHandler(
            stripeFeature: stripeFeature,
            verifier: verifier,
            processedEvents: processedEvents);

        var result = await handler.HandleAsync(
            BuildSucceededPayload("evt_disabled_1", 1000),
            Signature,
            CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        verifier.DidNotReceive().TryVerify(Arg.Any<string>(), Arg.Any<string>());
        await processedEvents.DidNotReceive().ExistsAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await processedEvents.DidNotReceive().TryAddAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenSignatureIsInvalid_Returns400WithoutReadingEvents()
    {
        var processedEvents = Substitute.For<IProcessedStripeEventRepository>();
        var verifier = Substitute.For<IStripeWebhookVerifier>();
        verifier.TryVerify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        var handler = CreateHandler(processedEvents: processedEvents, verifier: verifier);

        var result = await handler.HandleAsync(
            BuildSucceededPayload("evt_1", 1000),
            Signature,
            CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
        await processedEvents.DidNotReceive().ExistsAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenPayloadIsNotJson_Returns400()
    {
        var handler = CreateHandler();

        var result = await handler.HandleAsync("not-json", Signature, CancellationToken.None);

        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task HandleAsync_WhenEventWasAlreadyProcessed_Returns200WithoutExecuting()
    {
        var processedEvents = Substitute.For<IProcessedStripeEventRepository>();
        processedEvents.ExistsAsync("evt_1", Arg.Any<CancellationToken>()).Returns(true);
        var executor = Substitute.For<IWebhookTransactionExecutor>();
        var paymentRepository = Substitute.For<IPaymentRepository>();
        var handler = CreateHandler(
            processedEvents: processedEvents,
            executor: executor,
            paymentRepository: paymentRepository);

        var result = await handler.HandleAsync(
            BuildSucceededPayload("evt_1", 1000),
            Signature,
            CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        await executor.DidNotReceive().ExecutePaymentIntentSucceededAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Money>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        await paymentRepository.DidNotReceive().GetByProviderPaymentIdAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenPaymentIntentSucceeded_ExecutesAndPublishesEvents()
    {
        var payment = CreateStripePayment(Money.FromDecimal(10m));
        var paymentConfirmed = new PaymentConfirmed(
            payment.Id,
            payment.OrderId,
            payment.Amount,
            FixedUtcNow);
        var executor = Substitute.For<IWebhookTransactionExecutor>();
        executor
            .ExecutePaymentIntentSucceededAsync(
                "evt_1",
                "payment_intent.succeeded",
                payment.Id,
                Arg.Any<Money>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns(new WebhookExecutionResult(
                Processed: true,
                DomainEvents: new object[] { paymentConfirmed }));
        var mediator = Substitute.For<IMediator>();
        var handler = CreateHandler(
            paymentRepository: PaymentRepositoryReturning(payment),
            executor: executor,
            mediator: mediator);

        var result = await handler.HandleAsync(
            BuildSucceededPayload("evt_1", 1000),
            Signature,
            CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        await executor.Received(1).ExecutePaymentIntentSucceededAsync(
            "evt_1",
            "payment_intent.succeeded",
            payment.Id,
            Money.FromDecimal(10m),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).Publish(
            Arg.Is<DomainEventNotification<object>>(notification =>
                ReferenceEquals(notification.Event, paymentConfirmed)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenPaymentAlreadyConfirmed_Returns200WithoutExecuting()
    {
        var payment = CreateStripePayment(Money.FromDecimal(10m));
        payment.MarkConfirmed(FixedUtcNow);
        var processedEvents = Substitute.For<IProcessedStripeEventRepository>();
        var executor = Substitute.For<IWebhookTransactionExecutor>();
        var handler = CreateHandler(
            processedEvents: processedEvents,
            paymentRepository: PaymentRepositoryReturning(payment),
            executor: executor);

        var result = await handler.HandleAsync(
            BuildSucceededPayload("evt_1", 1000),
            Signature,
            CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        await executor.DidNotReceive().ExecutePaymentIntentSucceededAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Money>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        await processedEvents.Received(1).TryAddAsync(
            "evt_1",
            "payment_intent.succeeded",
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(PaymentStatus.Failed)]
    [InlineData(PaymentStatus.Refunded)]
    public async Task HandleAsync_WhenPaymentIsFailedOrRefunded_Returns200WithoutExecuting(
        PaymentStatus status)
    {
        var payment = CreateStripePayment(Money.FromDecimal(10m));

        if (status == PaymentStatus.Failed)
        {
            payment.MarkFailed();
        }
        else
        {
            payment.MarkConfirmed(FixedUtcNow);
            payment.AddRefund(payment.Amount, FixedUtcNow);
        }

        var executor = Substitute.For<IWebhookTransactionExecutor>();
        var handler = CreateHandler(
            paymentRepository: PaymentRepositoryReturning(payment),
            executor: executor);

        var result = await handler.HandleAsync(
            BuildSucceededPayload("evt_1", 1000),
            Signature,
            CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        await executor.DidNotReceive().ExecutePaymentIntentSucceededAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Money>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenPaymentIsNotFound_Returns200WithoutExecuting()
    {
        var processedEvents = Substitute.For<IProcessedStripeEventRepository>();
        var executor = Substitute.For<IWebhookTransactionExecutor>();
        var handler = CreateHandler(processedEvents: processedEvents, executor: executor);

        var result = await handler.HandleAsync(
            BuildSucceededPayload("evt_1", 1000),
            Signature,
            CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        await executor.DidNotReceive().ExecutePaymentIntentSucceededAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Money>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        await processedEvents.Received(1).TryAddAsync(
            "evt_1",
            "payment_intent.succeeded",
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenOrderIsNotCreated_Returns200WithoutExecuting()
    {
        var payment = CreateStripePayment(Money.FromDecimal(10m));
        var executor = Substitute.For<IWebhookTransactionExecutor>();
        var orderRepository = Substitute.For<IOrderRepository>();
        orderRepository
            .GetStatusAsync(payment.OrderId, Arg.Any<CancellationToken>())
            .Returns(OrderStatus.Cancelled);
        var handler = CreateHandler(
            paymentRepository: PaymentRepositoryReturning(payment),
            orderRepository: orderRepository,
            executor: executor);

        var result = await handler.HandleAsync(
            BuildSucceededPayload("evt_1", 1000),
            Signature,
            CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        await executor.DidNotReceive().ExecutePaymentIntentSucceededAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Money>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenAmountDoesNotMatch_Returns200WithoutExecuting()
    {
        var payment = CreateStripePayment(Money.FromDecimal(10m));
        var executor = Substitute.For<IWebhookTransactionExecutor>();
        var handler = CreateHandler(
            paymentRepository: PaymentRepositoryReturning(payment),
            executor: executor);

        var result = await handler.HandleAsync(
            BuildSucceededPayload("evt_1", 1001),
            Signature,
            CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        await executor.DidNotReceive().ExecutePaymentIntentSucceededAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Money>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenCurrencyIsNotUsd_Returns200WithoutExecuting()
    {
        var payment = CreateStripePayment(Money.FromDecimal(10m));
        var executor = Substitute.For<IWebhookTransactionExecutor>();
        var handler = CreateHandler(
            paymentRepository: PaymentRepositoryReturning(payment),
            executor: executor);

        var result = await handler.HandleAsync(
            BuildPayload("evt_1", "payment_intent.succeeded", ProviderPaymentId, 1000, "eur"),
            Signature,
            CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        await executor.DidNotReceive().ExecutePaymentIntentSucceededAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Money>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("charge.refunded")]
    [InlineData("refund.updated")]
    public async Task HandleAsync_WhenRefundEvent_AddsProcessedEventWithoutExecuting(string type)
    {
        var processedEvents = Substitute.For<IProcessedStripeEventRepository>();
        var executor = Substitute.For<IWebhookTransactionExecutor>();
        var handler = CreateHandler(processedEvents: processedEvents, executor: executor);

        var result = await handler.HandleAsync(
            BuildPayload("evt_refund_1", type, "ch_1", 1000, "usd", ProviderPaymentId),
            Signature,
            CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        await processedEvents.Received(1).TryAddAsync(
            "evt_refund_1",
            type,
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        await executor.DidNotReceive().ExecutePaymentIntentSucceededAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Money>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenTypeIsUnknown_AddsProcessedEventWithoutExecuting()
    {
        var processedEvents = Substitute.For<IProcessedStripeEventRepository>();
        var executor = Substitute.For<IWebhookTransactionExecutor>();
        var handler = CreateHandler(processedEvents: processedEvents, executor: executor);

        var result = await handler.HandleAsync(
            BuildPayload("evt_unknown_1", "customer.created", "cus_1", null, "usd"),
            Signature,
            CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        await processedEvents.Received(1).TryAddAsync(
            "evt_unknown_1",
            "customer.created",
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        await executor.DidNotReceive().ExecutePaymentIntentSucceededAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Money>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    private static WebhookHandler CreateHandler(
        IStripeFeature? stripeFeature = null,
        IStripeWebhookVerifier? verifier = null,
        IProcessedStripeEventRepository? processedEvents = null,
        IPaymentRepository? paymentRepository = null,
        IOrderRepository? orderRepository = null,
        IWebhookTransactionExecutor? executor = null,
        IMediator? mediator = null)
    {
        var feature = stripeFeature;
        if (feature is null)
        {
            feature = Substitute.For<IStripeFeature>();
            feature.Enabled.Returns(true);
        }

        var stripeVerifier = verifier;
        if (stripeVerifier is null)
        {
            stripeVerifier = Substitute.For<IStripeWebhookVerifier>();
            stripeVerifier.TryVerify(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        }

        var orderStatusRepository = orderRepository;
        if (orderStatusRepository is null)
        {
            orderStatusRepository = Substitute.For<IOrderRepository>();
            orderStatusRepository
                .GetStatusAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(OrderStatus.Created);
        }

        return new WebhookHandler(
            feature,
            stripeVerifier,
            processedEvents ?? Substitute.For<IProcessedStripeEventRepository>(),
            paymentRepository ?? Substitute.For<IPaymentRepository>(),
            orderStatusRepository,
            executor ?? Substitute.For<IWebhookTransactionExecutor>(),
            mediator ?? Substitute.For<IMediator>(),
            Substitute.For<ILogger<WebhookHandler>>());
    }

    private static IPaymentRepository PaymentRepositoryReturning(Payment payment)
    {
        var repository = Substitute.For<IPaymentRepository>();
        repository
            .GetByProviderPaymentIdAsync(ProviderPaymentId, Arg.Any<CancellationToken>())
            .Returns(payment);
        return repository;
    }

    private static Payment CreateStripePayment(Money amount)
    {
        var payment = Payment.Create(OrderId, amount, FixedUtcNow);
        payment.SetProvider("stripe");
        payment.SetProviderPaymentId(ProviderPaymentId);
        return payment;
    }

    private static string BuildSucceededPayload(string eventId, long amountInCents) =>
        BuildPayload(
            eventId,
            "payment_intent.succeeded",
            ProviderPaymentId,
            amountInCents,
            "usd");

    private static string BuildPayload(
        string eventId,
        string type,
        string? objectId,
        long? amountInCents,
        string currency,
        string? paymentIntentId = null)
    {
        return JsonSerializer.Serialize(new
        {
            id = eventId,
            type,
            data = new
            {
                @object = new
                {
                    id = objectId,
                    payment_intent = paymentIntentId,
                    amount = amountInCents,
                    currency
                }
            }
        });
    }
}

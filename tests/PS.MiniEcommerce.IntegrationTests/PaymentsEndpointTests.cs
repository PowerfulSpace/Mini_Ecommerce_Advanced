using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PS.MiniEcommerce.Application.Abstractions.Payments;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Domain.Payments;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.Infrastructure.Payments;
using PS.MiniEcommerce.Infrastructure.Persistence;
using PS.MiniEcommerce.Infrastructure.Seed;
using PS.MiniEcommerce.IntegrationTests.Fixtures;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class PaymentsEndpointTests : IAsyncLifetime
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const string Admin = "admin";
    private const string WirelessMouse = "Wireless Mouse";

    private readonly TestDatabaseFixture _fixture;
    private readonly NotificationCapture _capture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PaymentsEndpointTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
        _capture = new NotificationCapture();
        _factory = new CustomWebApplicationFactory(
            fixture.ConnectionString,
            notificationCapture: _capture);
        _client = _factory.CreateClient();
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Confirm_WhenPendingPayment_Returns200AndConsumesReserved()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);
        const string idempotencyKey = "confirm-success-1";

        var response = await ConfirmAsync(_client, order.PaymentId, idempotencyKey);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var payment = await response.Content.ReadFromJsonAsync<PaymentResponse>();
        Assert.NotNull(payment);
        Assert.Equal(order.PaymentId, payment.Id);
        Assert.Equal(order.OrderId, payment.OrderId);
        Assert.Equal("Confirmed", payment.Status);
        Assert.NotNull(payment.ConfirmedAtUtc);
        Assert.Equal(0m, payment.RefundedAmount);

        var storedPayment = await GetPaymentAsync(order.PaymentId);
        Assert.NotNull(storedPayment);
        Assert.Equal(PaymentStatus.Confirmed, storedPayment.Status);
        Assert.NotNull(storedPayment.ConfirmedAtUtc);
        Assert.Equal(OrderStatus.Paid, await GetOrderStatusAsync(order.OrderId));

        var stock = await GetStockAsync(WirelessMouse);
        Assert.Equal(99, stock.Available);
        Assert.Equal(0, stock.Reserved);

        var attempt = await GetPaymentAttemptAsync(idempotencyKey);
        Assert.NotNull(attempt);
        Assert.Equal(PaymentAttemptStatus.Completed, attempt.Status);
        Assert.Equal(200, attempt.ResponseStatusCode);
        Assert.Equal(order.PaymentId, attempt.PaymentId);
        Assert.NotNull(attempt.ResponsePayload);

        Assert.Single(_capture.Events.OfType<PaymentConfirmed>());
        Assert.Single(_capture.Events.OfType<OrderPaid>());

        var audits = await GetAuditLogsAsync();
        Assert.Contains(audits, audit => audit.Action == "PaymentConfirmed" && audit.EntityId == order.PaymentId);
        Assert.Contains(audits, audit => audit.Action == "OrderPaid" && audit.EntityId == order.OrderId);
    }

    [Fact]
    public async Task Confirm_WhenReplayingSameKey_ReturnsStoredResponseWithoutNewEventsOrAudit()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);
        const string idempotencyKey = "confirm-replay-1";

        var first = await ConfirmAsync(_client, order.PaymentId, idempotencyKey);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await first.Content.ReadAsStringAsync();

        var auditsBeforeReplay = await GetAuditLogsAsync();
        var second = await ConfirmAsync(_client, order.PaymentId, idempotencyKey);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(firstBody, await second.Content.ReadAsStringAsync());
        Assert.Single(await GetPaymentAttemptsByKeyAsync(idempotencyKey));
        Assert.Single(_capture.Events.OfType<PaymentConfirmed>());
        Assert.Single(_capture.Events.OfType<OrderPaid>());

        var auditsAfterReplay = await GetAuditLogsAsync();
        Assert.Equal(
            auditsBeforeReplay.Count(audit => audit.Action == "PaymentConfirmed"),
            auditsAfterReplay.Count(audit => audit.Action == "PaymentConfirmed"));
        Assert.Equal(
            auditsBeforeReplay.Count(audit => audit.Action == "OrderPaid"),
            auditsAfterReplay.Count(audit => audit.Action == "OrderPaid"));
    }

    [Fact]
    public async Task Confirm_WhenDifferentKeyAfterConfirmed_Returns409PaymentAlreadyConfirmed()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);

        var first = await ConfirmAsync(_client, order.PaymentId, "confirm-different-key-1");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await ConfirmAsync(_client, order.PaymentId, "confirm-different-key-2");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var error = await second.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("payment_already_confirmed", error.Code);
        Assert.Equal(OrderStatus.Paid, await GetOrderStatusAsync(order.OrderId));
    }

    [Fact]
    public async Task Confirm_WhenPaymentBelongsToAnotherUser_Returns404PaymentNotFound()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);
        const string idempotencyKey = "confirm-other-user-1";

        var response = await ConfirmAsync(_client, order.PaymentId, idempotencyKey, Bob);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("payment_not_found", error.Code);

        var payment = await GetPaymentAsync(order.PaymentId);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(OrderStatus.Created, await GetOrderStatusAsync(order.OrderId));

        var attempt = await GetPaymentAttemptAsync(idempotencyKey);
        Assert.NotNull(attempt);
        Assert.Equal(404, attempt.ResponseStatusCode);
    }

    [Fact]
    public async Task Confirm_WithoutIdempotencyKey_Returns400ValidationErrorAndDoesNotCreateAttempt()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);

        var response = await ConfirmAsync(_client, order.PaymentId, idempotencyKey: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("validation_error", error.Code);
        Assert.Empty(await GetPaymentAttemptsAsync());
    }

    [Fact]
    public async Task Confirm_WhenSameKeyHasDifferentFingerprint_Returns409IdempotencyKeyReused()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);
        const string idempotencyKey = "confirm-fingerprint-1";
        var userId = await GetUserIdAsync(SeedData.AliceEmail);
        var otherFingerprint = Enumerable.Repeat((byte)0xAA, 32).ToArray();

        await SeedPaymentAttemptAsync(
            userId,
            idempotencyKey,
            otherFingerprint,
            DateTime.UtcNow,
            TimeSpan.FromMinutes(1));

        var response = await ConfirmAsync(_client, order.PaymentId, idempotencyKey);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("idempotency_key_reused", error.Code);
        Assert.Equal(PaymentStatus.Pending, (await GetPaymentAsync(order.PaymentId))!.Status);
    }

    [Fact]
    public async Task Confirm_WhenAttemptLeaseIsExpired_ClaimsAndCompletes()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);
        const string idempotencyKey = "confirm-expired-lease-1";
        var userId = await GetUserIdAsync(SeedData.AliceEmail);

        await SeedPaymentAttemptAsync(
            userId,
            idempotencyKey,
            ComputeFingerprint(order.PaymentId),
            DateTime.UtcNow.AddMinutes(-1),
            TimeSpan.FromSeconds(30));

        var response = await ConfirmAsync(_client, order.PaymentId, idempotencyKey);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var attempt = await GetPaymentAttemptAsync(idempotencyKey);
        Assert.NotNull(attempt);
        Assert.Equal(PaymentAttemptStatus.Completed, attempt.Status);
        Assert.Equal(200, attempt.ResponseStatusCode);
        Assert.Equal(OrderStatus.Paid, await GetOrderStatusAsync(order.OrderId));
    }

    [Fact]
    public async Task Confirm_WhenAttemptIsInProgressWithActiveLease_Returns503()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);
        const string idempotencyKey = "confirm-active-lease-1";
        var userId = await GetUserIdAsync(SeedData.AliceEmail);

        await SeedPaymentAttemptAsync(
            userId,
            idempotencyKey,
            ComputeFingerprint(order.PaymentId),
            DateTime.UtcNow,
            TimeSpan.FromMinutes(1));

        var response = await ConfirmAsync(_client, order.PaymentId, idempotencyKey);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("idempotency_in_progress", error.Code);

        var attempt = await GetPaymentAttemptAsync(idempotencyKey);
        Assert.NotNull(attempt);
        Assert.Equal(PaymentAttemptStatus.InProgress, attempt.Status);
        Assert.Equal(PaymentStatus.Pending, (await GetPaymentAsync(order.PaymentId))!.Status);
    }

    [Fact]
    public async Task Confirm_WhenProviderFails_Returns409AndRetryWithSameKeySucceeds()
    {
        var capture = new NotificationCapture();
        var provider = new MockPaymentProvider(new[]
        {
            PaymentProviderResult.Failed(),
            PaymentProviderResult.Succeeded()
        });
        using var factory = CreateFactory(provider, capture);
        using var client = factory.CreateClient();

        var order = await CreateOrderAsync(client, Alice, WirelessMouse, quantity: 1);
        const string idempotencyKey = "confirm-provider-retry-1";

        var first = await ConfirmAsync(client, order.PaymentId, idempotencyKey);

        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
        var firstError = await first.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(firstError);
        Assert.Equal("payment_failed", firstError.Code);

        var failedPayment = await GetPaymentAsync(order.PaymentId);
        Assert.NotNull(failedPayment);
        Assert.Equal(PaymentStatus.Failed, failedPayment.Status);
        Assert.Equal(OrderStatus.Created, await GetOrderStatusAsync(order.OrderId));
        Assert.Equal(1, (await GetStockAsync(WirelessMouse)).Reserved);

        var failedAttempt = await GetPaymentAttemptAsync(idempotencyKey);
        Assert.NotNull(failedAttempt);
        Assert.Equal(PaymentAttemptStatus.Completed, failedAttempt.Status);
        Assert.Equal(409, failedAttempt.ResponseStatusCode);

        var second = await ConfirmAsync(client, order.PaymentId, idempotencyKey);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var payment = await GetPaymentAsync(order.PaymentId);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
        Assert.Equal(OrderStatus.Paid, await GetOrderStatusAsync(order.OrderId));
        Assert.Equal(0, (await GetStockAsync(WirelessMouse)).Reserved);

        var retriedAttempt = await GetPaymentAttemptAsync(idempotencyKey);
        Assert.NotNull(retriedAttempt);
        Assert.Equal(PaymentAttemptStatus.Completed, retriedAttempt.Status);
        Assert.Equal(200, retriedAttempt.ResponseStatusCode);
        Assert.Single(capture.Events.OfType<PaymentConfirmed>());
        Assert.Single(capture.Events.OfType<OrderPaid>());
    }

    [Fact]
    public async Task Confirm_WhenProviderFails_NewKeyCanRetrySamePayment()
    {
        var capture = new NotificationCapture();
        var provider = new MockPaymentProvider(new[]
        {
            PaymentProviderResult.Failed(),
            PaymentProviderResult.Succeeded()
        });
        using var factory = CreateFactory(provider, capture);
        using var client = factory.CreateClient();

        var order = await CreateOrderAsync(client, Alice, WirelessMouse, quantity: 1);

        var first = await ConfirmAsync(client, order.PaymentId, "confirm-provider-new-key-1");
        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);

        var second = await ConfirmAsync(client, order.PaymentId, "confirm-provider-new-key-2");

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(PaymentStatus.Confirmed, (await GetPaymentAsync(order.PaymentId))!.Status);
        Assert.Equal(OrderStatus.Paid, await GetOrderStatusAsync(order.OrderId));
        Assert.Equal(2, (await GetPaymentAttemptsAsync()).Count);
    }

    [Fact]
    public async Task Confirm_WhenPaymentIsRefunded_Returns409PaymentAlreadyConfirmed()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);
        await SetPaymentRefundedAsync(order.PaymentId);

        var response = await ConfirmAsync(_client, order.PaymentId, "confirm-refunded-1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("payment_already_confirmed", error.Code);
    }

    [Fact]
    public async Task Confirm_WhenOrderIsCancelled_Returns409InvalidOrderTransition()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);
        var cancelRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/orders/{order.OrderId}/cancel");
        cancelRequest.Headers.Add("X-Test-User", Alice);
        cancelRequest.Headers.TryAddWithoutValidation("If-Match", order.ETag);
        var cancelResponse = await _client.SendAsync(cancelRequest);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        var response = await ConfirmAsync(_client, order.PaymentId, "confirm-cancelled-order-1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("invalid_order_transition", error.Code);
        Assert.Equal(OrderStatus.Cancelled, await GetOrderStatusAsync(order.OrderId));
        Assert.Equal(0, (await GetStockAsync(WirelessMouse)).Reserved);
    }

    [Fact]
    public async Task Confirm_AfterPartialCancellation_ConsumesOnlyRemainingQuantity()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 2);
        var orderDetails = await GetOrderDetailsAsync(order.OrderId);
        var item = Assert.Single(orderDetails.Items);
        var orderEtag = await GetOrderETagAsync(order.OrderId);

        var cancelRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/orders/{order.OrderId}/cancel")
        {
            Content = JsonContent.Create(new
            {
                items = new[] { new { orderItemId = item.OrderItemId, quantity = 1 } }
            })
        };
        cancelRequest.Headers.Add("X-Test-User", Alice);
        cancelRequest.Headers.TryAddWithoutValidation("If-Match", orderEtag);
        var cancelResponse = await _client.SendAsync(cancelRequest);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        var stockAfterPartialCancel = await GetStockAsync(WirelessMouse);
        Assert.Equal(99, stockAfterPartialCancel.Available);
        Assert.Equal(1, stockAfterPartialCancel.Reserved);

        var response = await ConfirmAsync(_client, order.PaymentId, "confirm-partial-cancel-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stockAfterConfirm = await GetStockAsync(WirelessMouse);
        Assert.Equal(99, stockAfterConfirm.Available);
        Assert.Equal(0, stockAfterConfirm.Reserved);
        Assert.Equal(OrderStatus.Paid, await GetOrderStatusAsync(order.OrderId));
    }

    [Fact]
    public async Task Refund_WhenConfirmedPartial_Returns200AndCreatesRefund()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);
        var confirm = await ConfirmAsync(_client, order.PaymentId, "refund-partial-confirm-1");
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        const decimal amount = 5m;
        const string reason = "damaged";
        var adminUserId = await GetUserIdAsync(SeedData.AdminEmail);

        var response = await RefundAsync(
            _client,
            order.PaymentId,
            amount,
            reason,
            "refund-partial-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var payment = await response.Content.ReadFromJsonAsync<PaymentResponse>();
        Assert.NotNull(payment);
        Assert.Equal(order.PaymentId, payment.Id);
        Assert.Equal(order.OrderId, payment.OrderId);
        Assert.Equal(amount, payment.RefundedAmount);
        Assert.Equal("Confirmed", payment.Status);
        Assert.Null(payment.RefundedAtUtc);

        var stored = await GetPaymentWithRefundsAsync(order.PaymentId);
        Assert.NotNull(stored);
        Assert.Equal(PaymentStatus.Confirmed, stored.Status);
        Assert.Equal(amount, stored.RefundedAmount.Amount);
        Assert.Null(stored.RefundedAtUtc);
        var refund = Assert.Single(stored.Refunds);
        Assert.Equal(order.PaymentId, refund.PaymentId);
        Assert.Equal(amount, refund.Amount.Amount);
        Assert.Equal(reason, refund.Reason);

        var attempt = await GetPaymentAttemptAsync("refund-partial-1");
        Assert.NotNull(attempt);
        Assert.Equal(PaymentAttemptStatus.Completed, attempt.Status);
        Assert.Equal(200, attempt.ResponseStatusCode);
        Assert.Equal(order.PaymentId, attempt.PaymentId);

        var paymentRefunded = Assert.Single(_capture.Events.OfType<PaymentRefunded>());
        Assert.Equal(order.PaymentId, paymentRefunded.PaymentId);
        Assert.Equal(amount, paymentRefunded.Amount.Amount);
        Assert.Equal(reason, paymentRefunded.Reason);

        var audit = Assert.Single(
            await GetAuditLogsAsync(),
            log => log.Action == "PaymentRefunded");
        Assert.Equal("Payment", audit.EntityType);
        Assert.Equal(order.PaymentId, audit.EntityId);
        Assert.Equal(adminUserId, audit.ActorUserId);
        using var payload = System.Text.Json.JsonDocument.Parse(audit.Payload!);
        Assert.Equal(order.OrderId, payload.RootElement.GetProperty("orderId").GetGuid());
        Assert.Equal(amount, payload.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal(reason, payload.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Refund_WhenMultiplePartialsReachFull_RefundsOnlyOnFullAmount()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);
        await ConfirmAsync(_client, order.PaymentId, "refund-multiple-confirm-1");
        var payment = await GetPaymentAsync(order.PaymentId);
        Assert.NotNull(payment);

        const decimal firstAmount = 5m;
        var secondAmount = payment.Amount.Amount - firstAmount;

        var first = await RefundAsync(
            _client,
            order.PaymentId,
            firstAmount,
            "first",
            "refund-multiple-1");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var afterFirst = await GetPaymentWithRefundsAsync(order.PaymentId);
        Assert.NotNull(afterFirst);
        Assert.Equal(PaymentStatus.Confirmed, afterFirst.Status);
        Assert.Equal(firstAmount, afterFirst.RefundedAmount.Amount);

        var second = await RefundAsync(
            _client,
            order.PaymentId,
            secondAmount,
            "second",
            "refund-multiple-2");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var afterSecond = await GetPaymentWithRefundsAsync(order.PaymentId);
        Assert.NotNull(afterSecond);
        Assert.Equal(PaymentStatus.Refunded, afterSecond.Status);
        Assert.Equal(payment.Amount.Amount, afterSecond.RefundedAmount.Amount);
        Assert.NotNull(afterSecond.RefundedAtUtc);
        Assert.Equal(2, afterSecond.Refunds.Count);
        Assert.Equal(2, _capture.Events.OfType<PaymentRefunded>().Count());
    }

    [Fact]
    public async Task Refund_WhenPaymentIsPending_Returns409RefundNotAllowed()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);

        var response = await RefundAsync(
            _client,
            order.PaymentId,
            1m,
            null,
            "refund-pending-1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("refund_not_allowed", error.Code);

        var stored = await GetPaymentWithRefundsAsync(order.PaymentId);
        Assert.NotNull(stored);
        Assert.Equal(PaymentStatus.Pending, stored.Status);
        Assert.Equal(0m, stored.RefundedAmount.Amount);
        Assert.Empty(stored.Refunds);

        var attempt = await GetPaymentAttemptAsync("refund-pending-1");
        Assert.NotNull(attempt);
        Assert.Equal(PaymentAttemptStatus.Completed, attempt.Status);
        Assert.Equal(409, attempt.ResponseStatusCode);
    }

    [Fact]
    public async Task Refund_WhenAmountExceedsRemaining_Returns409RefundNotAllowed()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);
        await ConfirmAsync(_client, order.PaymentId, "refund-exceeds-confirm-1");
        var payment = await GetPaymentAsync(order.PaymentId);
        Assert.NotNull(payment);

        var response = await RefundAsync(
            _client,
            order.PaymentId,
            payment.Amount.Amount + 0.01m,
            null,
            "refund-exceeds-1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("refund_not_allowed", error.Code);

        var stored = await GetPaymentWithRefundsAsync(order.PaymentId);
        Assert.NotNull(stored);
        Assert.Equal(0m, stored.RefundedAmount.Amount);
        Assert.Empty(stored.Refunds);
    }

    [Fact]
    public async Task Refund_WhenPaymentAlreadyRefunded_Returns409RefundNotAllowed()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);
        await ConfirmAsync(_client, order.PaymentId, "refund-already-confirm-1");
        var payment = await GetPaymentAsync(order.PaymentId);
        Assert.NotNull(payment);

        var first = await RefundAsync(
            _client,
            order.PaymentId,
            payment.Amount.Amount,
            null,
            "refund-already-1");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await RefundAsync(
            _client,
            order.PaymentId,
            1m,
            null,
            "refund-already-2");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var error = await second.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("refund_not_allowed", error.Code);
    }

    [Fact]
    public async Task Refund_WhenSameKeyHasDifferentFingerprint_Returns409IdempotencyKeyReused()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);
        await ConfirmAsync(_client, order.PaymentId, "refund-fingerprint-confirm-1");
        const string idempotencyKey = "refund-fingerprint-1";

        var first = await RefundAsync(
            _client,
            order.PaymentId,
            5m,
            "damaged",
            idempotencyKey);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await RefundAsync(
            _client,
            order.PaymentId,
            6m,
            "damaged",
            idempotencyKey);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var error = await second.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("idempotency_key_reused", error.Code);

        var stored = await GetPaymentWithRefundsAsync(order.PaymentId);
        Assert.NotNull(stored);
        Assert.Equal(5m, stored.RefundedAmount.Amount);
        Assert.Single(stored.Refunds);
    }

    [Fact]
    public async Task Refund_WhenReplayingSameKey_ReturnsStoredResponseWithoutNewEventsOrAudit()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);
        await ConfirmAsync(_client, order.PaymentId, "refund-replay-confirm-1");
        const string idempotencyKey = "refund-replay-1";

        var first = await RefundAsync(
            _client,
            order.PaymentId,
            5m,
            "damaged",
            idempotencyKey);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await first.Content.ReadAsStringAsync();

        var auditsBeforeReplay = await GetAuditLogsAsync();
        var second = await RefundAsync(
            _client,
            order.PaymentId,
            5m,
            "damaged",
            idempotencyKey);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(firstBody, await second.Content.ReadAsStringAsync());
        Assert.Single(await GetPaymentAttemptsByKeyAsync(idempotencyKey));
        Assert.Single(_capture.Events.OfType<PaymentRefunded>());

        var stored = await GetPaymentWithRefundsAsync(order.PaymentId);
        Assert.NotNull(stored);
        Assert.Equal(5m, stored.RefundedAmount.Amount);
        Assert.Single(stored.Refunds);

        var auditsAfterReplay = await GetAuditLogsAsync();
        Assert.Equal(
            auditsBeforeReplay.Count(audit => audit.Action == "PaymentRefunded"),
            auditsAfterReplay.Count(audit => audit.Action == "PaymentRefunded"));
    }

    [Fact]
    public async Task Refund_WhenAttemptIsInProgressWithActiveLease_Returns503()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);
        await ConfirmAsync(_client, order.PaymentId, "refund-active-confirm-1");
        const string idempotencyKey = "refund-active-lease-1";
        var userId = await GetUserIdAsync(SeedData.AdminEmail);

        await SeedPaymentAttemptAsync(
            userId,
            idempotencyKey,
            ComputeRefundFingerprint(order.PaymentId, 5m, null),
            DateTime.UtcNow,
            TimeSpan.FromMinutes(1));

        var response = await RefundAsync(
            _client,
            order.PaymentId,
            5m,
            null,
            idempotencyKey);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("idempotency_in_progress", error.Code);

        var attempt = await GetPaymentAttemptAsync(idempotencyKey);
        Assert.NotNull(attempt);
        Assert.Equal(PaymentAttemptStatus.InProgress, attempt.Status);

        var stored = await GetPaymentWithRefundsAsync(order.PaymentId);
        Assert.NotNull(stored);
        Assert.Equal(0m, stored.RefundedAmount.Amount);
    }

    [Fact]
    public async Task Refund_WhenAttemptLeaseIsExpired_ClaimsAndCompletes()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);
        await ConfirmAsync(_client, order.PaymentId, "refund-expired-confirm-1");
        const string idempotencyKey = "refund-expired-lease-1";
        var userId = await GetUserIdAsync(SeedData.AdminEmail);

        await SeedPaymentAttemptAsync(
            userId,
            idempotencyKey,
            ComputeRefundFingerprint(order.PaymentId, 5m, null),
            DateTime.UtcNow.AddMinutes(-1),
            TimeSpan.FromSeconds(30));

        var response = await RefundAsync(
            _client,
            order.PaymentId,
            5m,
            null,
            idempotencyKey);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var attempt = await GetPaymentAttemptAsync(idempotencyKey);
        Assert.NotNull(attempt);
        Assert.Equal(PaymentAttemptStatus.Completed, attempt.Status);
        Assert.Equal(200, attempt.ResponseStatusCode);

        var stored = await GetPaymentWithRefundsAsync(order.PaymentId);
        Assert.NotNull(stored);
        Assert.Equal(5m, stored.RefundedAmount.Amount);
    }

    [Fact]
    public async Task Refund_WhenAttemptsAreConcurrent_BothSucceedWithoutLostUpdate()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);
        await ConfirmAsync(_client, order.PaymentId, "refund-concurrent-confirm-1");

        var responses = await Task.WhenAll(
            Task.Run(() => RefundAsync(_client, order.PaymentId, 5m, null, "refund-concurrent-1")),
            Task.Run(() => RefundAsync(_client, order.PaymentId, 6m, null, "refund-concurrent-2")));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        var stored = await GetPaymentWithRefundsAsync(order.PaymentId);
        Assert.NotNull(stored);
        Assert.Equal(11m, stored.RefundedAmount.Amount);
        Assert.Equal(PaymentStatus.Confirmed, stored.Status);
        Assert.Equal(2, stored.Refunds.Count);
    }

    [Theory]
    [InlineData(Alice)]
    [InlineData(Bob)]
    public async Task Refund_WhenUserIsNotAdmin_Returns403(string user)
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);

        var response = await RefundAsync(
            _client,
            order.PaymentId,
            1m,
            null,
            "refund-non-admin-1",
            user);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("forbidden", error.Code);
    }

    [Fact]
    public async Task Refund_WhenEnvironmentIsProduction_Returns404()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);

        using var factory = new CustomWebApplicationFactory(
            _fixture.ConnectionString,
            environment: "Production");
        using var client = factory.CreateClient();

        var response = await RefundAsync(
            client,
            order.PaymentId,
            1m,
            null,
            "refund-production-1");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Refund_WithoutIdempotencyKey_Returns400ValidationErrorAndDoesNotCreateAttempt()
    {
        var order = await CreateOrderAsync(_client, Alice, WirelessMouse, quantity: 1);
        await ConfirmAsync(_client, order.PaymentId, "refund-no-key-confirm-1");
        var attemptsBefore = (await GetPaymentAttemptsAsync()).Count;

        var response = await RefundAsync(
            _client,
            order.PaymentId,
            5m,
            null,
            idempotencyKey: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("validation_error", error.Code);
        Assert.Equal(attemptsBefore, (await GetPaymentAttemptsAsync()).Count);

        var stored = await GetPaymentWithRefundsAsync(order.PaymentId);
        Assert.NotNull(stored);
        Assert.Equal(0m, stored.RefundedAmount.Amount);
        Assert.Empty(stored.Refunds);
    }

    private CustomWebApplicationFactory CreateFactory(
        MockPaymentProvider provider,
        NotificationCapture capture)
    {
        return new CustomWebApplicationFactory(
            _fixture.ConnectionString,
            notificationCapture: capture,
            configureTestServices: services =>
            {
                services.RemoveAll<IPaymentProvider>();
                services.AddSingleton<IPaymentProvider>(provider);
            });
    }

    private async Task<CreatedOrder> CreateOrderAsync(
        HttpClient client,
        string testUser,
        string productName,
        int quantity)
    {
        var productId = await GetActiveProductIdAsync(productName);
        var cartEtag = await GetCartETagAsync(client, testUser);

        var addRequest = new HttpRequestMessage(HttpMethod.Post, "/api/cart/items")
        {
            Content = JsonContent.Create(new { productId, quantity })
        };
        addRequest.Headers.Add("X-Test-User", testUser);
        addRequest.Headers.TryAddWithoutValidation("If-Match", cartEtag);
        var addResponse = await client.SendAsync(addRequest);
        addResponse.EnsureSuccessStatusCode();

        var orderRequest = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        orderRequest.Headers.Add("X-Test-User", testUser);
        orderRequest.Headers.Add("Idempotency-Key", $"checkout-payment-{Guid.NewGuid():N}");
        orderRequest.Headers.TryAddWithoutValidation("If-Match", addResponse.Headers.ETag!.Tag);
        var orderResponse = await client.SendAsync(orderRequest);
        orderResponse.EnsureSuccessStatusCode();

        var order = await orderResponse.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(order);
        Assert.NotNull(order.PaymentId);

        return new CreatedOrder(
            order.Id,
            order.PaymentId.Value,
            orderResponse.Headers.ETag!.Tag);
    }

    private async Task<string> GetCartETagAsync(HttpClient client, string testUser)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/cart");
        request.Headers.Add("X-Test-User", testUser);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return response.Headers.ETag!.Tag;
    }

    private static Task<HttpResponseMessage> ConfirmAsync(
        HttpClient client,
        Guid paymentId,
        string? idempotencyKey,
        string testUser = Alice)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/payments/{paymentId}/confirm");
        request.Headers.Add("X-Test-User", testUser);

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> RefundAsync(
        HttpClient client,
        Guid paymentId,
        decimal amount,
        string? reason,
        string? idempotencyKey,
        string testUser = Admin)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/payments/{paymentId}/refund")
        {
            Content = JsonContent.Create(new { amount, reason })
        };
        request.Headers.Add("X-Test-User", testUser);

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return client.SendAsync(request);
    }

    private async Task<Guid> GetActiveProductIdAsync(string name)
    {
        await using var context = CreateContext();
        return await context.Products
            .Where(product => product.Name == name && product.IsActive)
            .Select(product => product.Id)
            .FirstAsync();
    }

    private async Task<Guid> GetUserIdAsync(string email)
    {
        await using var context = CreateContext();
        return await context.Users
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .FirstAsync();
    }

    private async Task<Payment?> GetPaymentAsync(Guid paymentId)
    {
        await using var context = CreateContext();
        return await context.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(payment => payment.Id == paymentId);
    }

    private async Task<Payment?> GetPaymentWithRefundsAsync(Guid paymentId)
    {
        await using var context = CreateContext();
        return await context.Payments
            .AsNoTracking()
            .Include(payment => payment.Refunds)
            .FirstOrDefaultAsync(payment => payment.Id == paymentId);
    }

    private async Task<OrderStatus> GetOrderStatusAsync(Guid orderId)
    {
        await using var context = CreateContext();
        return await context.Orders
            .AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(order => order.Status)
            .FirstAsync();
    }

    private async Task<StockSnapshot> GetStockAsync(string name)
    {
        await using var context = CreateContext();
        var stock = await context.Products
            .AsNoTracking()
            .Where(product => product.Name == name)
            .Select(product => new { product.Available, product.Reserved })
            .FirstAsync();

        return new StockSnapshot(stock.Available, stock.Reserved);
    }

    private async Task<PaymentAttempt?> GetPaymentAttemptAsync(string idempotencyKey)
    {
        await using var context = CreateContext();
        return await context.PaymentAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(attempt => attempt.IdempotencyKey == idempotencyKey);
    }

    private async Task<List<PaymentAttempt>> GetPaymentAttemptsByKeyAsync(string idempotencyKey)
    {
        await using var context = CreateContext();
        return await context.PaymentAttempts
            .AsNoTracking()
            .Where(attempt => attempt.IdempotencyKey == idempotencyKey)
            .ToListAsync();
    }

    private async Task<List<PaymentAttempt>> GetPaymentAttemptsAsync()
    {
        await using var context = CreateContext();
        return await context.PaymentAttempts
            .AsNoTracking()
            .ToListAsync();
    }

    private async Task<List<AuditLog>> GetAuditLogsAsync()
    {
        await using var context = CreateContext();
        return await context.AuditLogs
            .AsNoTracking()
            .ToListAsync();
    }

    private async Task SeedPaymentAttemptAsync(
        Guid userId,
        string idempotencyKey,
        byte[] fingerprint,
        DateTime utcNow,
        TimeSpan leaseDuration)
    {
        await using var context = CreateContext();
        context.PaymentAttempts.Add(
            PaymentAttempt.Create(userId, idempotencyKey, fingerprint, utcNow, leaseDuration));
        await context.SaveChangesAsync();
    }

    private async Task SetPaymentRefundedAsync(Guid paymentId)
    {
        await using var context = CreateContext();
        var payment = await context.Payments
            .Include(payment => payment.Refunds)
            .FirstAsync(payment => payment.Id == paymentId);

        payment.MarkConfirmed(DateTime.UtcNow);
        payment.AddRefund(payment.Amount, DateTime.UtcNow);
        await context.SaveChangesAsync();
    }

    private async Task<OrderDetailsResponse> GetOrderDetailsAsync(Guid orderId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{orderId}");
        request.Headers.Add("X-Test-User", Alice);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var order = await response.Content.ReadFromJsonAsync<OrderDetailsResponse>();
        Assert.NotNull(order);
        return order;
    }

    private async Task<string> GetOrderETagAsync(Guid orderId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{orderId}");
        request.Headers.Add("X-Test-User", Alice);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return response.Headers.ETag!.Tag;
    }

    private static byte[] ComputeFingerprint(Guid paymentId)
    {
        var id = paymentId.ToString();
        var input = $"POST /api/payments/{id}/confirm\n{id}";
        return SHA256.HashData(Encoding.UTF8.GetBytes(input));
    }

    private static byte[] ComputeRefundFingerprint(
        Guid paymentId,
        decimal amount,
        string? reason)
    {
        var id = paymentId.ToString();
        var normalizedAmount = decimal
            .Round(amount, 2, MidpointRounding.AwayFromZero)
            .ToString("0.00", CultureInfo.InvariantCulture);
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? string.Empty : reason.Trim();
        var input = $"POST /api/payments/{id}/refund\n{id}\n{normalizedAmount}\n{normalizedReason}";
        return SHA256.HashData(Encoding.UTF8.GetBytes(input));
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options);

    private sealed record CreatedOrder(Guid OrderId, Guid PaymentId, string ETag);

    private sealed record StockSnapshot(int Available, int Reserved);

    private sealed record PaymentResponse(
        Guid Id,
        Guid OrderId,
        decimal Amount,
        decimal RefundedAmount,
        string Status,
        DateTime CreatedAtUtc,
        DateTime? ConfirmedAtUtc,
        DateTime? RefundedAtUtc);

    private sealed record OrderResponse(
        Guid Id,
        string Status,
        Guid? PaymentId);

    private sealed record OrderDetailsResponse(
        Guid Id,
        string Status,
        List<OrderItemResponse> Items);

    private sealed record OrderItemResponse(
        Guid OrderItemId,
        Guid ProductId,
        int Quantity,
        int CancelledQuantity);

    private sealed record ErrorResponse(string Code, string Detail);
}

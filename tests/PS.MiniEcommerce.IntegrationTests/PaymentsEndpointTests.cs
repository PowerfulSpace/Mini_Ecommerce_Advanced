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

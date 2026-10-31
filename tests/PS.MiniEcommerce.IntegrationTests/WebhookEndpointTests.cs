using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Payments;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.Infrastructure.Payments;
using PS.MiniEcommerce.Infrastructure.Persistence;
using PS.MiniEcommerce.IntegrationTests.Fixtures;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class WebhookEndpointTests : IAsyncLifetime
{
    private const string Alice = "alice";
    private const string WirelessMouse = "Wireless Mouse";
    private const string WebhookSecret = "whsec_test_secret_123456";

    private readonly TestDatabaseFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public WebhookEndpointTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
        _factory = CreateStripeFactory();
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
    public async Task Webhook_PaymentIntentSucceeded_ConfirmsPaymentAndWritesAuditAndOutbox()
    {
        var order = await CreateOrderAsync(_client);
        var eventId = "evt_succeeded_1";
        var payload = BuildSucceededPayload(
            eventId,
            order.ProviderPaymentId!,
            ToCents(order.Amount));

        var response = await SendWebhookAsync(_client, payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payment = await GetPaymentAsync(order.PaymentId);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
        Assert.NotNull(payment.ConfirmedAtUtc);
        Assert.Equal(OrderStatus.Paid, await GetOrderStatusAsync(order.OrderId));

        var stock = await GetStockAsync(WirelessMouse);
        Assert.Equal(99, stock.Available);
        Assert.Equal(0, stock.Reserved);

        Assert.True(await ProcessedEventExistsAsync(eventId));

        var outboxTypes = await GetOutboxEventTypesAsync();
        Assert.Contains(typeof(PS.MiniEcommerce.Domain.Events.PaymentConfirmed).FullName!, outboxTypes);
        Assert.Contains(typeof(PS.MiniEcommerce.Domain.Events.OrderPaid).FullName!, outboxTypes);

        var audits = await GetAuditLogsAsync();
        Assert.Single(audits, audit => audit.Action == "PaymentConfirmed" && audit.EntityId == order.PaymentId);
        Assert.Single(audits, audit => audit.Action == "OrderPaid" && audit.EntityId == order.OrderId);
    }

    [Fact]
    public async Task Webhook_DuplicateEventId_IsIdempotent()
    {
        var order = await CreateOrderAsync(_client);
        var eventId = "evt_duplicate_1";
        var payload = BuildSucceededPayload(
            eventId,
            order.ProviderPaymentId!,
            ToCents(order.Amount));

        var first = await SendWebhookAsync(_client, payload);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var auditsAfterFirst = (await GetAuditLogsAsync()).Count;
        var outboxAfterFirst = (await GetOutboxEventTypesAsync()).Count;

        var second = await SendWebhookAsync(_client, payload);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(auditsAfterFirst, (await GetAuditLogsAsync()).Count);
        Assert.Equal(outboxAfterFirst, (await GetOutboxEventTypesAsync()).Count);
    }

    [Fact]
    public async Task Webhook_InvalidSignature_Returns400AndChangesNothing()
    {
        var order = await CreateOrderAsync(_client);
        var eventId = "evt_invalid_signature_1";
        var payload = BuildSucceededPayload(
            eventId,
            order.ProviderPaymentId!,
            ToCents(order.Amount));

        var auditsBefore = (await GetAuditLogsAsync()).Count;
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var wrongSignature = ComputeSignature(timestamp, payload, "whsec_wrong_secret_123456");
        var request = CreateWebhookRequest(payload, $"t={timestamp},v1={wrongSignature}");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payment = await GetPaymentAsync(order.PaymentId);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(OrderStatus.Created, await GetOrderStatusAsync(order.OrderId));
        Assert.False(await ProcessedEventExistsAsync(eventId));

        var audits = await GetAuditLogsAsync();
        Assert.Equal(auditsBefore, audits.Count);
        Assert.DoesNotContain(audits, audit => audit.Action == "PaymentConfirmed");
    }

    [Fact]
    public async Task Webhook_WhenPaymentAlreadyConfirmed_Returns200WithoutNewAudit()
    {
        var order = await CreateOrderAsync(_client);

        var first = await SendWebhookAsync(
            _client,
            BuildSucceededPayload("evt_confirm_first_1", order.ProviderPaymentId!, ToCents(order.Amount)));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var auditsAfterFirst = (await GetAuditLogsAsync()).Count;

        var second = await SendWebhookAsync(
            _client,
            BuildSucceededPayload("evt_confirm_second_1", order.ProviderPaymentId!, ToCents(order.Amount)));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(auditsAfterFirst, (await GetAuditLogsAsync()).Count);
        Assert.True(await ProcessedEventExistsAsync("evt_confirm_second_1"));
    }

    [Fact]
    public async Task Webhook_WhenOrderWasCancelled_Returns200WithoutConfirming()
    {
        var order = await CreateOrderAsync(_client);

        var cancelRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/orders/{order.OrderId}/cancel");
        cancelRequest.Headers.Add("X-Test-User", Alice);
        cancelRequest.Headers.TryAddWithoutValidation("If-Match", order.ETag);
        var cancelResponse = await _client.SendAsync(cancelRequest);
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);

        var response = await SendWebhookAsync(
            _client,
            BuildSucceededPayload("evt_cancelled_1", order.ProviderPaymentId!, ToCents(order.Amount)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payment = await GetPaymentAsync(order.PaymentId);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(OrderStatus.Cancelled, await GetOrderStatusAsync(order.OrderId));
        Assert.True(await ProcessedEventExistsAsync("evt_cancelled_1"));
    }

    [Fact]
    public async Task Webhook_ChargeRefunded_IsNoOpButRecordsEvent()
    {
        var order = await CreateOrderAsync(_client);
        var payload = JsonSerializer.Serialize(new
        {
            id = "evt_charge_refunded_1",
            type = "charge.refunded",
            data = new
            {
                @object = new
                {
                    id = "ch_1",
                    payment_intent = order.ProviderPaymentId,
                    amount = ToCents(order.Amount),
                    currency = "usd"
                }
            }
        });

        var response = await SendWebhookAsync(_client, payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payment = await GetPaymentAsync(order.PaymentId);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(0m, payment.RefundedAmount.Amount);
        Assert.True(await ProcessedEventExistsAsync("evt_charge_refunded_1"));
        Assert.DoesNotContain(
            await GetAuditLogsAsync(),
            audit => audit.Action == "PaymentConfirmed" || audit.Action == "OrderPaid");
    }

    [Fact]
    public async Task Webhook_UnknownType_RecordsEvent()
    {
        var order = await CreateOrderAsync(_client);
        var payload = JsonSerializer.Serialize(new
        {
            id = "evt_unknown_1",
            type = "customer.created",
            data = new
            {
                @object = new
                {
                    id = "cus_1"
                }
            }
        });

        var response = await SendWebhookAsync(_client, payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payment = await GetPaymentAsync(order.PaymentId);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.True(await ProcessedEventExistsAsync("evt_unknown_1"));
    }

    [Fact]
    public async Task Webhook_WhenPaymentIsNotFound_Returns200AndRecordsEvent()
    {
        var payload = BuildSucceededPayload("evt_missing_payment_1", "pi_mock_missing", 1999);

        var response = await SendWebhookAsync(_client, payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await ProcessedEventExistsAsync("evt_missing_payment_1"));
        Assert.Empty(await GetAuditLogsAsync());
    }

    [Fact]
    public async Task Webhook_WhenStripeDisabled_AcknowledgesWithoutChanges()
    {
        using var factory = new CustomWebApplicationFactory(_fixture.ConnectionString);
        using var client = factory.CreateClient();

        var order = await CreateOrderAsync(client);
        Assert.Equal("mock", order.Provider);
        Assert.Equal("mock", order.ProviderPaymentId);

        var payload = BuildSucceededPayload(
            "evt_disabled_1",
            "pi_mock_not_found",
            ToCents(order.Amount));

        var response = await SendWebhookAsync(client, payload);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(await ProcessedEventExistsAsync("evt_disabled_1"));

        var payment = await GetPaymentAsync(order.PaymentId);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    private CustomWebApplicationFactory CreateStripeFactory()
    {
        return new CustomWebApplicationFactory(
            _fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.Configure<StripeOptions>(options =>
                {
                    options.Enabled = true;
                    options.WebhookSecret = WebhookSecret;
                });
            });
    }

    private static async Task<HttpResponseMessage> SendWebhookAsync(
        HttpClient client,
        string payload)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = ComputeSignature(timestamp, payload, WebhookSecret);
        var request = CreateWebhookRequest(payload, $"t={timestamp},v1={signature}");
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage CreateWebhookRequest(string payload, string signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/stripe")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Stripe-Signature", signature);
        return request;
    }

    private static string ComputeSignature(long timestamp, string payload, string secret)
    {
        var signedPayload = $"{timestamp.ToString(CultureInfo.InvariantCulture)}.{payload}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));

        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload)))
            .ToLowerInvariant();
    }

    private static string BuildSucceededPayload(
        string eventId,
        string providerPaymentId,
        long amountInCents)
    {
        return JsonSerializer.Serialize(new
        {
            id = eventId,
            type = "payment_intent.succeeded",
            data = new
            {
                @object = new
                {
                    id = providerPaymentId,
                    amount = amountInCents,
                    currency = "usd",
                    status = "succeeded"
                }
            }
        });
    }

    private static long ToCents(Money amount) =>
        decimal.ToInt64(amount.Amount * 100m);

    private async Task<CreatedOrder> CreateOrderAsync(HttpClient client)
    {
        var productId = await GetActiveProductIdAsync();
        var cartEtag = await GetCartETagAsync(client);

        var addRequest = new HttpRequestMessage(HttpMethod.Post, "/api/cart/items")
        {
            Content = JsonContent.Create(new { productId, quantity = 1 })
        };
        addRequest.Headers.Add("X-Test-User", Alice);
        addRequest.Headers.TryAddWithoutValidation("If-Match", cartEtag);
        var addResponse = await client.SendAsync(addRequest);
        addResponse.EnsureSuccessStatusCode();

        var orderRequest = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        orderRequest.Headers.Add("X-Test-User", Alice);
        orderRequest.Headers.Add("Idempotency-Key", $"webhook-checkout-{Guid.NewGuid():N}");
        orderRequest.Headers.TryAddWithoutValidation("If-Match", addResponse.Headers.ETag!.Tag);
        var orderResponse = await client.SendAsync(orderRequest);
        orderResponse.EnsureSuccessStatusCode();

        var order = await orderResponse.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(order);
        Assert.NotNull(order.PaymentId);

        var payment = await GetPaymentAsync(order.PaymentId.Value);
        Assert.NotNull(payment);

        return new CreatedOrder(
            order.Id,
            order.PaymentId.Value,
            payment.Amount,
            payment.Provider,
            payment.ProviderPaymentId,
            orderResponse.Headers.ETag!.Tag);
    }

    private async Task<string> GetCartETagAsync(HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/cart");
        request.Headers.Add("X-Test-User", Alice);
        var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return response.Headers.ETag!.Tag;
    }

    private async Task<Guid> GetActiveProductIdAsync()
    {
        await using var context = CreateContext();
        return await context.Products
            .Where(product => product.Name == WirelessMouse && product.IsActive)
            .Select(product => product.Id)
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

    private async Task<bool> ProcessedEventExistsAsync(string eventId)
    {
        await using var context = CreateContext();
        return await context.ProcessedStripeEvents
            .AsNoTracking()
            .AnyAsync(processedEvent => processedEvent.EventId == eventId);
    }

    private async Task<List<string>> GetOutboxEventTypesAsync()
    {
        await using var context = CreateContext();
        return await context.OutboxMessages
            .AsNoTracking()
            .Select(message => message.EventType)
            .ToListAsync();
    }

    private async Task<List<AuditLog>> GetAuditLogsAsync()
    {
        await using var context = CreateContext();
        return await context.AuditLogs
            .AsNoTracking()
            .ToListAsync();
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options);

    private sealed record CreatedOrder(
        Guid OrderId,
        Guid PaymentId,
        Money Amount,
        string Provider,
        string? ProviderPaymentId,
        string ETag);

    private sealed record StockSnapshot(int Available, int Reserved);

    private sealed record OrderResponse(
        Guid Id,
        string Status,
        Guid? PaymentId);
}

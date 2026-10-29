using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.Infrastructure.Persistence;
using PS.MiniEcommerce.IntegrationTests.Fixtures;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class OutboxWriteTests : IAsyncLifetime
{
    private const string Alice = "alice";
    private const string Admin = "admin";
    private const string WirelessMouse = "Wireless Mouse";

    private readonly TestDatabaseFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public OutboxWriteTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
        _factory = new CustomWebApplicationFactory(fixture.ConnectionString);
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
    public async Task CheckoutConfirmAndRefund_WriteOutboxMessagesWithJsonPayloads()
    {
        var order = await CreateOrderAsync(quantity: 1);

        var confirm = await SendAsync(
            HttpMethod.Post,
            $"/api/payments/{order.PaymentId}/confirm",
            Alice,
            idempotencyKey: "outbox-confirm-1");
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        var refund = await SendAsync(
            HttpMethod.Post,
            $"/api/payments/{order.PaymentId}/refund",
            Admin,
            new { amount = 5m, reason = "customer_request" },
            "outbox-refund-1");
        Assert.Equal(HttpStatusCode.OK, refund.StatusCode);

        await using var context = CreateContext();
        var messages = await context.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        Assert.Equal(4, messages.Count);

        var orderPlaced = messages.Single(
            message => message.EventType == typeof(OrderPlaced).FullName);
        Assert.Null(orderPlaced.ProcessedAtUtc);
        Assert.Equal(0, orderPlaced.Attempts);
        Assert.Null(orderPlaced.LastError);

        using (var payload = JsonDocument.Parse(orderPlaced.Payload))
        {
            Assert.Equal(19.99m, payload.RootElement.GetProperty("totalAmount").GetDecimal());
            Assert.Equal(1, payload.RootElement.GetProperty("itemCount").GetInt32());
        }

        var orderPaid = messages.Single(
            message => message.EventType == typeof(OrderPaid).FullName);

        using (var paidPayload = JsonDocument.Parse(orderPaid.Payload))
        {
            Assert.Equal(order.OrderId, paidPayload.RootElement.GetProperty("orderId").GetGuid());
        }

        Assert.Single(
            messages,
            message => message.EventType == typeof(PaymentConfirmed).FullName);

        var paymentRefunded = messages.Single(
            message => message.EventType == typeof(PaymentRefunded).FullName);

        using var refundPayload = JsonDocument.Parse(paymentRefunded.Payload);
        Assert.Equal(5m, refundPayload.RootElement.GetProperty("amount").GetDecimal());
        Assert.Equal("customer_request", refundPayload.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task CancelShipDeliverAndReturn_WriteOutboxMessages()
    {
        var cancelled = await CreateOrderAsync(quantity: 1);
        var cancel = await SendAsync(
            HttpMethod.Post,
            $"/api/orders/{cancelled.OrderId}/cancel",
            Alice,
            ifMatch: cancelled.OrderETag);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);

        var shipped = await CreateOrderAsync(quantity: 1);
        await ConfirmAsync(shipped.PaymentId, "outbox-ship-confirm-1");

        var ship = await SendAsync(
            HttpMethod.Post,
            $"/api/admin/orders/{shipped.OrderId}/ship",
            Admin);
        Assert.Equal(HttpStatusCode.OK, ship.StatusCode);

        var deliver = await SendAsync(
            HttpMethod.Post,
            $"/api/admin/orders/{shipped.OrderId}/deliver",
            Admin);
        Assert.Equal(HttpStatusCode.OK, deliver.StatusCode);

        var returned = await SendAsync(
            HttpMethod.Post,
            $"/api/admin/orders/{shipped.OrderId}/return",
            Admin);
        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);

        await using var context = CreateContext();
        var eventTypes = await context.OutboxMessages
            .AsNoTracking()
            .Select(message => message.EventType)
            .ToListAsync();

        Assert.Contains(typeof(OrderCancelled).FullName, eventTypes);
        Assert.Contains(typeof(OrderShipped).FullName, eventTypes);
        Assert.Contains(typeof(OrderDelivered).FullName, eventTypes);
        Assert.Contains(typeof(OrderReturned).FullName, eventTypes);
        Assert.Contains(typeof(OrderPlaced).FullName, eventTypes);
        Assert.Contains(typeof(OrderPaid).FullName, eventTypes);
        Assert.Contains(typeof(PaymentConfirmed).FullName, eventTypes);
        Assert.Contains(typeof(PaymentRefunded).FullName, eventTypes);
    }

    [Fact]
    public async Task AdminStockAndDeactivate_WriteOutboxMessages()
    {
        var productId = await GetActiveProductIdAsync();

        var stock = await SendAsync(
            HttpMethod.Post,
            $"/api/admin/products/{productId}/stock",
            Admin,
            new { available = 7 });
        Assert.Equal(HttpStatusCode.OK, stock.StatusCode);

        var deactivate = await SendAsync(
            HttpMethod.Post,
            $"/api/admin/products/{productId}/deactivate",
            Admin);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        await using var context = CreateContext();
        var messages = await context.OutboxMessages
            .AsNoTracking()
            .ToListAsync();

        var stockAdjusted = messages.Single(
            message => message.EventType == typeof(StockAdjusted).FullName);

        using (var payload = JsonDocument.Parse(stockAdjusted.Payload))
        {
            Assert.Equal(7, payload.RootElement.GetProperty("newAvailable").GetInt32());
        }

        Assert.Single(
            messages,
            message => message.EventType == typeof(ProductDeactivated).FullName);
    }

    private async Task<CreatedOrder> CreateOrderAsync(int quantity)
    {
        var productId = await GetActiveProductIdAsync();

        var cartRequest = new HttpRequestMessage(HttpMethod.Get, "/api/cart");
        cartRequest.Headers.Add("X-Test-User", Alice);
        var cartResponse = await _client.SendAsync(cartRequest);
        cartResponse.EnsureSuccessStatusCode();

        var addRequest = new HttpRequestMessage(HttpMethod.Post, "/api/cart/items")
        {
            Content = JsonContent.Create(new { productId, quantity })
        };
        addRequest.Headers.Add("X-Test-User", Alice);
        addRequest.Headers.Add("If-Match", cartResponse.Headers.ETag!.Tag);
        var addResponse = await _client.SendAsync(addRequest);
        addResponse.EnsureSuccessStatusCode();

        var orderRequest = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        orderRequest.Headers.Add("X-Test-User", Alice);
        orderRequest.Headers.Add("Idempotency-Key", $"outbox-{Guid.NewGuid():N}");
        orderRequest.Headers.Add("If-Match", addResponse.Headers.ETag!.Tag);
        var orderResponse = await _client.SendAsync(orderRequest);
        orderResponse.EnsureSuccessStatusCode();

        var order = await orderResponse.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(order);
        Assert.NotNull(order.PaymentId);

        return new CreatedOrder(
            order.Id,
            order.PaymentId.Value,
            orderResponse.Headers.ETag!.Tag);
    }

    private async Task ConfirmAsync(Guid paymentId, string idempotencyKey)
    {
        var response = await SendAsync(
            HttpMethod.Post,
            $"/api/payments/{paymentId}/confirm",
            Alice,
            idempotencyKey: idempotencyKey);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string user,
        object? body = null,
        string? idempotencyKey = null,
        string? ifMatch = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-User", user);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (ifMatch is not null)
        {
            request.Headers.Add("If-Match", ifMatch);
        }

        return _client.SendAsync(request);
    }

    private async Task<Guid> GetActiveProductIdAsync()
    {
        await using var context = CreateContext();
        return await context.Products
            .Where(product => product.Name == WirelessMouse && product.IsActive)
            .Select(product => product.Id)
            .FirstAsync();
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options);

    private sealed record CreatedOrder(Guid OrderId, Guid PaymentId, string OrderETag);

    private sealed record OrderResponse(Guid Id, Guid? PaymentId);
}

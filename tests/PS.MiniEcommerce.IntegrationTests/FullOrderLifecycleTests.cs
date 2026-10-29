using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Payments;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.Infrastructure.Persistence;
using PS.MiniEcommerce.Infrastructure.Seed;
using PS.MiniEcommerce.IntegrationTests.Fixtures;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class FullOrderLifecycleTests : IAsyncLifetime
{
    private const string Alice = "alice";
    private const string Admin = "admin";
    private const string WirelessMouse = "Wireless Mouse";

    private readonly TestDatabaseFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public FullOrderLifecycleTests(TestDatabaseFixture fixture)
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
    public async Task FullOrderLifecycle_CheckoutConfirmShipDeliverReturn_UpdatesStateAndAudits()
    {
        var productId = await GetActiveProductIdAsync(WirelessMouse);
        var stockBefore = await GetStockAsync(WirelessMouse);
        var aliceUserId = await GetUserIdAsync(SeedData.AliceEmail);
        var adminUserId = await GetUserIdAsync(SeedData.AdminEmail);

        var cartETag = await AddItemToAliceCartAsync(productId, quantity: 1);
        var checkout = await SendCheckoutAsync("lifecycle-checkout-1", cartETag);

        Assert.Equal(HttpStatusCode.Created, checkout.StatusCode);
        var order = await checkout.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(order);
        Assert.Equal("Created", order.Status);
        Assert.NotNull(order.PaymentId);

        var orderId = order.Id;
        var paymentId = order.PaymentId.Value;

        var paymentAfterCheckout = await GetPaymentAsync(paymentId);
        Assert.NotNull(paymentAfterCheckout);
        Assert.Equal(PaymentStatus.Pending, paymentAfterCheckout.Status);
        Assert.Equal(OrderStatus.Created, await GetOrderStatusAsync(orderId));
        Assert.Equal(stockBefore.Available - 1, (await GetStockAsync(WirelessMouse)).Available);
        Assert.Equal(stockBefore.Reserved + 1, (await GetStockAsync(WirelessMouse)).Reserved);
        await AssertAuditAsync("OrderPlaced", orderId, aliceUserId);

        var confirm = await ConfirmAsync(paymentId, "lifecycle-confirm-1");

        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var confirmedPayment = await confirm.Content.ReadFromJsonAsync<PaymentResponse>();
        Assert.NotNull(confirmedPayment);
        Assert.Equal("Confirmed", confirmedPayment.Status);
        Assert.Equal(OrderStatus.Paid, await GetOrderStatusAsync(orderId));
        Assert.Equal(PaymentStatus.Confirmed, (await GetPaymentAsync(paymentId))!.Status);
        Assert.Equal(stockBefore.Available - 1, (await GetStockAsync(WirelessMouse)).Available);
        Assert.Equal(0, (await GetStockAsync(WirelessMouse)).Reserved);
        await AssertAuditAsync("PaymentConfirmed", paymentId, aliceUserId);
        await AssertAuditAsync("OrderPaid", orderId, aliceUserId);

        var ship = await SendAdminAsync("ship", orderId);

        Assert.Equal(HttpStatusCode.OK, ship.StatusCode);
        Assert.Equal(OrderStatus.Shipped, await GetOrderStatusAsync(orderId));
        Assert.Equal(PaymentStatus.Confirmed, (await GetPaymentAsync(paymentId))!.Status);
        Assert.Equal(stockBefore.Available - 1, (await GetStockAsync(WirelessMouse)).Available);
        Assert.Equal(0, (await GetStockAsync(WirelessMouse)).Reserved);
        await AssertAuditAsync("OrderShipped", orderId, adminUserId);

        var deliver = await SendAdminAsync("deliver", orderId);

        Assert.Equal(HttpStatusCode.OK, deliver.StatusCode);
        Assert.Equal(OrderStatus.Delivered, await GetOrderStatusAsync(orderId));
        Assert.Equal(PaymentStatus.Confirmed, (await GetPaymentAsync(paymentId))!.Status);
        Assert.Equal(stockBefore.Available - 1, (await GetStockAsync(WirelessMouse)).Available);
        Assert.Equal(0, (await GetStockAsync(WirelessMouse)).Reserved);
        await AssertAuditAsync("OrderDelivered", orderId, adminUserId);

        var returned = await SendAdminAsync("return", orderId);

        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
        var returnedOrder = await returned.Content.ReadFromJsonAsync<OrderDetailResponse>();
        Assert.NotNull(returnedOrder);
        Assert.Equal("Returned", returnedOrder.Status);
        Assert.Equal(OrderStatus.Returned, await GetOrderStatusAsync(orderId));

        var refundedPayment = await GetPaymentAsync(paymentId);
        Assert.NotNull(refundedPayment);
        Assert.Equal(PaymentStatus.Refunded, refundedPayment.Status);
        Assert.Equal(refundedPayment.Amount.Amount, refundedPayment.RefundedAmount.Amount);
        Assert.NotNull(refundedPayment.RefundedAtUtc);

        var stockAfterReturn = await GetStockAsync(WirelessMouse);
        Assert.Equal(stockBefore.Available, stockAfterReturn.Available);
        Assert.Equal(stockBefore.Reserved, stockAfterReturn.Reserved);

        await AssertAuditAsync("OrderReturned", orderId, adminUserId);
        await AssertAuditAsync("PaymentRefunded", paymentId, adminUserId);
    }

    private async Task<string> AddItemToAliceCartAsync(Guid productId, int quantity)
    {
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

        return addResponse.Headers.ETag!.Tag;
    }

    private Task<HttpResponseMessage> SendCheckoutAsync(string idempotencyKey, string ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.Add("X-Test-User", Alice);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add("If-Match", ifMatch);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> ConfirmAsync(Guid paymentId, string idempotencyKey)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/payments/{paymentId}/confirm");
        request.Headers.Add("X-Test-User", Alice);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> SendAdminAsync(string action, Guid orderId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/admin/orders/{orderId}/{action}");
        request.Headers.Add("X-Test-User", Admin);
        return _client.SendAsync(request);
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
            .SingleAsync();
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

    private async Task<Payment?> GetPaymentAsync(Guid paymentId)
    {
        await using var context = CreateContext();
        return await context.Payments
            .AsNoTracking()
            .FirstOrDefaultAsync(payment => payment.Id == paymentId);
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

    private async Task AssertAuditAsync(string action, Guid entityId, Guid actorUserId)
    {
        await using var context = CreateContext();
        var auditLog = await context.AuditLogs
            .AsNoTracking()
            .SingleAsync(log => log.Action == action);

        Assert.Equal(entityId, auditLog.EntityId);
        Assert.Equal(actorUserId, auditLog.ActorUserId);
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options);

    private sealed record StockSnapshot(int Available, int Reserved);

    private sealed record OrderResponse(Guid Id, string Status, Guid? PaymentId);

    private sealed record OrderDetailResponse(Guid Id, string Status);

    private sealed record PaymentResponse(
        Guid Id,
        Guid OrderId,
        decimal Amount,
        decimal RefundedAmount,
        string Status);
}

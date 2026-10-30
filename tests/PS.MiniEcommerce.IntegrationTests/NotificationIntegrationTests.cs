using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PS.MiniEcommerce.Application.Abstractions.Outbox;
using PS.MiniEcommerce.Application.Notifications;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.Infrastructure.Persistence;
using PS.MiniEcommerce.Infrastructure.Seed;
using PS.MiniEcommerce.IntegrationTests.Fixtures;
using Xunit.Sdk;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class NotificationIntegrationTests : IAsyncLifetime
{
    private const string Alice = "alice";
    private const string Admin = "admin";
    private const string WirelessMouse = "Wireless Mouse";

    private readonly TestDatabaseFixture _fixture;

    private CustomWebApplicationFactory? _factory;
    private HttpClient? _client;

    public NotificationIntegrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        _client?.Dispose();
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Checkout_CreatesOrderPlacedNotification()
    {
        var store = Start();

        var order = await CreateOrderAsync();

        var notification = await WaitForNotificationAsync(store, nameof(OrderPlaced));

        Assert.Equal(await GetUserIdAsync(SeedData.AliceEmail), notification.UserId);
        Assert.Equal(order.OrderId, notification.EntityId);
        Assert.Equal(order.OrderId, notification.OrderId);
        Assert.Equal(order.TotalAmount, notification.Amount);
    }

    [Fact]
    public async Task Checkout_DoesNotDuplicateAuditLogWithNotificationSubscriber()
    {
        var store = Start();

        var order = await CreateOrderAsync();

        await WaitForNotificationAsync(store, nameof(OrderPlaced));
        await WaitUntilAsync(
            async () => await CountAuditLogsAsync(nameof(OrderPlaced), order.OrderId) == 1,
            "OrderPlaced audit log was not written exactly once.");

        Assert.Equal(1, await CountAuditLogsAsync(nameof(OrderPlaced), order.OrderId));
    }

    [Fact]
    public async Task Confirm_CreatesPaymentConfirmedNotification()
    {
        var store = Start();
        var order = await CreateOrderAsync();

        var response = await ConfirmAsync(order.PaymentId, "notif-confirm-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var notification = await WaitForNotificationAsync(store, nameof(PaymentConfirmed));

        Assert.Equal(await GetUserIdAsync(SeedData.AliceEmail), notification.UserId);
        Assert.Equal(order.PaymentId, notification.EntityId);
        Assert.Equal(order.OrderId, notification.OrderId);
        Assert.Equal(order.TotalAmount, notification.Amount);
    }

    [Fact]
    public async Task OrderPaid_IsNotNotifiedButAudited()
    {
        var store = Start();
        var order = await CreateOrderAsync();

        var response = await ConfirmAsync(order.PaymentId, "notif-order-paid-1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await WaitForNotificationAsync(store, nameof(PaymentConfirmed));
        await WaitUntilAsync(
            async () => await IsProcessedAsync(typeof(OrderPaid).FullName!),
            "OrderPaid outbox message was not processed.");
        await WaitUntilAsync(
            async () => await CountAuditLogsAsync(nameof(OrderPaid), order.OrderId) == 1,
            "OrderPaid audit log was not written.");

        Assert.DoesNotContain(store.GetAll(), notification => notification.EventType == nameof(OrderPaid));
        Assert.True(await CountAuditLogsAsync(nameof(OrderPaid), order.OrderId) == 1);
    }

    [Fact]
    public async Task AdminShipDeliverReturn_CreatesOrderNotifications()
    {
        var store = Start();
        var order = await CreateOrderAsync();
        var confirm = await ConfirmAsync(order.PaymentId, "notif-ship-confirm-1");
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        await WaitForNotificationAsync(store, nameof(PaymentConfirmed));

        var ship = await SendAdminAsync(HttpMethod.Post, $"/api/admin/orders/{order.OrderId}/ship");
        Assert.Equal(HttpStatusCode.OK, ship.StatusCode);
        var shipped = await WaitForNotificationAsync(store, nameof(OrderShipped));
        Assert.Equal(order.OrderId, shipped.EntityId);
        Assert.Equal(order.OrderId, shipped.OrderId);
        Assert.Equal(await GetUserIdAsync(SeedData.AliceEmail), shipped.UserId);

        var deliver = await SendAdminAsync(HttpMethod.Post, $"/api/admin/orders/{order.OrderId}/deliver");
        Assert.Equal(HttpStatusCode.OK, deliver.StatusCode);
        var delivered = await WaitForNotificationAsync(store, nameof(OrderDelivered));
        Assert.Equal(order.OrderId, delivered.EntityId);
        Assert.Equal(order.OrderId, delivered.OrderId);
        Assert.Equal(await GetUserIdAsync(SeedData.AliceEmail), delivered.UserId);

        var returnResponse = await SendAdminAsync(HttpMethod.Post, $"/api/admin/orders/{order.OrderId}/return");
        Assert.Equal(HttpStatusCode.OK, returnResponse.StatusCode);
        var returned = await WaitForNotificationAsync(store, nameof(OrderReturned));
        Assert.Equal(order.OrderId, returned.EntityId);
        Assert.Equal(order.OrderId, returned.OrderId);
        Assert.Equal(await GetUserIdAsync(SeedData.AliceEmail), returned.UserId);

        var refunded = await WaitForNotificationAsync(store, nameof(PaymentRefunded));
        Assert.Equal(order.PaymentId, refunded.EntityId);
        Assert.Equal(order.OrderId, refunded.OrderId);
        Assert.Equal(await GetUserIdAsync(SeedData.AliceEmail), refunded.UserId);
        Assert.Equal(order.TotalAmount, refunded.Amount);
    }

    [Fact]
    public async Task FullCancel_CreatesOrderCancelledNotification()
    {
        var store = Start();
        var order = await CreateOrderAsync();

        var response = await CancelAsync(order.OrderId, order.OrderETag);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var notification = await WaitForNotificationAsync(store, nameof(OrderCancelled));
        Assert.Equal(order.OrderId, notification.EntityId);
        Assert.Equal(order.OrderId, notification.OrderId);
        Assert.Equal(await GetUserIdAsync(SeedData.AliceEmail), notification.UserId);
        Assert.Contains("full cancellation: True", notification.Message);
    }

    [Fact]
    public async Task PartialCancel_CreatesOrderCancelledNotification()
    {
        var store = Start();
        var order = await CreateOrderAsync(quantity: 2);
        var orderItemId = await GetOrderItemIdAsync(order.OrderId);

        var response = await CancelAsync(
            order.OrderId,
            order.OrderETag,
            new { items = new[] { new { orderItemId, quantity = 1 } } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var notification = await WaitForNotificationAsync(store, nameof(OrderCancelled));
        Assert.Equal(order.OrderId, notification.EntityId);
        Assert.Equal(order.OrderId, notification.OrderId);
        Assert.Contains("full cancellation: False", notification.Message);
    }

    [Fact]
    public async Task AdminRefund_CreatesPaymentRefundedNotification()
    {
        var store = Start();
        var order = await CreateOrderAsync();
        var confirm = await ConfirmAsync(order.PaymentId, "notif-refund-confirm-1");
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        await WaitForNotificationAsync(store, nameof(PaymentConfirmed));

        var response = await RefundAsync(order.PaymentId, 5m, "notif-refund-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var notification = await WaitForNotificationAsync(store, nameof(PaymentRefunded));
        Assert.Equal(order.PaymentId, notification.EntityId);
        Assert.Equal(order.OrderId, notification.OrderId);
        Assert.Equal(await GetUserIdAsync(SeedData.AliceEmail), notification.UserId);
        Assert.Equal(5m, notification.Amount);
    }

    [Fact]
    public async Task AdminStockAndDeactivate_DoNotCreateNotifications()
    {
        var store = Start();
        var productId = await GetActiveProductIdAsync();

        var stock = await SendAdminAsync(
            HttpMethod.Post,
            $"/api/admin/products/{productId}/stock",
            new { available = 5 });
        Assert.Equal(HttpStatusCode.OK, stock.StatusCode);

        var deactivate = await SendAdminAsync(
            HttpMethod.Post,
            $"/api/admin/products/{productId}/deactivate");
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        await WaitUntilAsync(
            async () => await IsProcessedAsync(typeof(StockAdjusted).FullName!),
            "StockAdjusted outbox message was not processed.");
        await WaitUntilAsync(
            async () => await IsProcessedAsync(typeof(ProductDeactivated).FullName!),
            "ProductDeactivated outbox message was not processed.");

        Assert.Empty(store.GetAll());
    }

    [Fact]
    public async Task NotificationSubscriber_DeduplicatesRepeatedDelivery()
    {
        var store = Start();
        var userId = await GetUserIdAsync(SeedData.AliceEmail);
        var domainEvent = new OrderPlaced(
            Guid.NewGuid(),
            userId,
            Money.FromDecimal(19.99m),
            1,
            DateTime.UtcNow);

        await using var scope = _factory!.Services.CreateAsyncScope();
        var subscriber = scope.ServiceProvider
            .GetServices<IOutboxSubscriber>()
            .OfType<NotificationSubscriber>()
            .Single();

        await subscriber.HandleAsync(domainEvent, CancellationToken.None);
        await subscriber.HandleAsync(domainEvent, CancellationToken.None);

        var notification = Assert.Single(store.GetAll());
        Assert.Equal(nameof(OrderPlaced), notification.EventType);
        Assert.Equal(domainEvent.OrderId, notification.EntityId);
        Assert.Equal(domainEvent.OccurredAtUtc, notification.OccurredAtUtc);
    }

    private TestNotificationStore Start()
    {
        var store = new TestNotificationStore();

        _factory = new CustomWebApplicationFactory(
            _fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.RemoveAll<INotificationStore>();
                services.AddSingleton<INotificationStore>(store);
            },
            enableOutboxDispatcher: true);

        _client = _factory.CreateClient();

        return store;
    }

    private async Task<CreatedOrder> CreateOrderAsync(int quantity = 1)
    {
        var productId = await GetActiveProductIdAsync();

        var cartRequest = new HttpRequestMessage(HttpMethod.Get, "/api/cart");
        cartRequest.Headers.Add("X-Test-User", Alice);
        var cartResponse = await _client!.SendAsync(cartRequest);
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
        orderRequest.Headers.Add("Idempotency-Key", $"notif-{Guid.NewGuid():N}");
        orderRequest.Headers.Add("If-Match", addResponse.Headers.ETag!.Tag);
        var orderResponse = await _client.SendAsync(orderRequest);
        orderResponse.EnsureSuccessStatusCode();

        var order = await orderResponse.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(order);
        Assert.NotNull(order.PaymentId);
        Assert.NotNull(orderResponse.Headers.ETag);

        return new CreatedOrder(
            order.Id,
            order.PaymentId.Value,
            order.TotalAmount,
            orderResponse.Headers.ETag!.Tag);
    }

    private Task<HttpResponseMessage> ConfirmAsync(Guid paymentId, string idempotencyKey)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/payments/{paymentId}/confirm");
        request.Headers.Add("X-Test-User", Alice);
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        return _client!.SendAsync(request);
    }

    private Task<HttpResponseMessage> RefundAsync(Guid paymentId, decimal amount, string idempotencyKey)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/payments/{paymentId}/refund")
        {
            Content = JsonContent.Create(new { amount, reason = "customer_request" })
        };
        request.Headers.Add("X-Test-User", Admin);
        request.Headers.Add("Idempotency-Key", idempotencyKey);

        return _client!.SendAsync(request);
    }

    private Task<HttpResponseMessage> CancelAsync(
        Guid orderId,
        string ifMatch,
        object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/cancel");
        request.Headers.Add("X-Test-User", Alice);
        request.Headers.Add("If-Match", ifMatch);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _client!.SendAsync(request);
    }

    private Task<HttpResponseMessage> SendAdminAsync(
        HttpMethod method,
        string path,
        object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-User", Admin);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _client!.SendAsync(request);
    }

    private async Task<Guid> GetActiveProductIdAsync()
    {
        await using var context = CreateContext();
        return await context.Products
            .Where(product => product.Name == WirelessMouse && product.IsActive)
            .Select(product => product.Id)
            .FirstAsync();
    }

    private async Task<Guid> GetOrderItemIdAsync(Guid orderId)
    {
        await using var context = CreateContext();
        return await context.OrderItems
            .Where(item => item.OrderId == orderId)
            .Select(item => item.Id)
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

    private async Task<int> CountAuditLogsAsync(string action, Guid entityId)
    {
        await using var context = CreateContext();
        return await context.AuditLogs
            .CountAsync(log => log.Action == action && log.EntityId == entityId);
    }

    private async Task<bool> IsProcessedAsync(string eventType)
    {
        await using var context = CreateContext();
        return await context.OutboxMessages
            .AsNoTracking()
            .AnyAsync(message =>
                message.EventType == eventType && message.ProcessedAtUtc != null);
    }

    private static async Task<Notification> WaitForNotificationAsync(
        TestNotificationStore store,
        string eventType)
    {
        Notification? notification = null;

        await WaitUntilAsync(
            () =>
            {
                notification = store.GetAll()
                    .FirstOrDefault(candidate => candidate.EventType == eventType);
                return Task.FromResult(notification is not null);
            },
            $"Notification {eventType} was not stored.");

        return notification!;
    }

    private static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        string failureMessage,
        int timeoutMs = 15000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(200);
        }

        throw new XunitException(failureMessage);
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options);

    private sealed record CreatedOrder(
        Guid OrderId,
        Guid PaymentId,
        decimal TotalAmount,
        string OrderETag);

    private sealed record OrderResponse(Guid Id, decimal TotalAmount, Guid? PaymentId);
}

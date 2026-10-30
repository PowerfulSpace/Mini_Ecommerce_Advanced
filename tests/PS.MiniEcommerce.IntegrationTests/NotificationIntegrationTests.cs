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
        var (sender, store) = Start();
        var testSender = Assert.IsType<TestNotificationSender>(sender);

        var order = await CreateOrderAsync();

        var sent = await WaitForNotificationAsync(testSender, nameof(OrderPlaced));

        Assert.Equal(SeedData.AliceEmail, sent.Email);
        Assert.Equal(await GetUserIdAsync(SeedData.AliceEmail), sent.Notification.UserId);
        Assert.Equal(order.OrderId, sent.Notification.EntityId);
        Assert.Equal(order.OrderId, sent.Notification.OrderId);
        Assert.Equal(order.TotalAmount, sent.Notification.Amount);
        Assert.Contains(store.GetAll(), notification => notification.EventType == nameof(OrderPlaced));
    }

    [Fact]
    public async Task Checkout_DoesNotDuplicateAuditLogWithNotificationSubscriber()
    {
        var (sender, store) = Start();
        var testSender = Assert.IsType<TestNotificationSender>(sender);

        var order = await CreateOrderAsync();

        await WaitForNotificationAsync(testSender, nameof(OrderPlaced));
        await WaitUntilAsync(
            async () => await CountAuditLogsAsync(nameof(OrderPlaced), order.OrderId) == 1,
            "OrderPlaced audit log was not written exactly once.");

        Assert.Equal(1, await CountAuditLogsAsync(nameof(OrderPlaced), order.OrderId));
    }

    [Fact]
    public async Task Confirm_CreatesPaymentConfirmedNotification()
    {
        var (sender, store) = Start();
        var testSender = Assert.IsType<TestNotificationSender>(sender);
        var order = await CreateOrderAsync();

        var response = await ConfirmAsync(order.PaymentId, "notif-confirm-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = await WaitForNotificationAsync(testSender, nameof(PaymentConfirmed));

        Assert.Equal(SeedData.AliceEmail, sent.Email);
        Assert.Equal(await GetUserIdAsync(SeedData.AliceEmail), sent.Notification.UserId);
        Assert.Equal(order.PaymentId, sent.Notification.EntityId);
        Assert.Equal(order.OrderId, sent.Notification.OrderId);
        Assert.Equal(order.TotalAmount, sent.Notification.Amount);
        Assert.Contains(store.GetAll(), notification => notification.EventType == nameof(PaymentConfirmed));
    }

    [Fact]
    public async Task OrderPaid_IsNotNotifiedButAudited()
    {
        var (sender, store) = Start();
        var testSender = Assert.IsType<TestNotificationSender>(sender);
        var order = await CreateOrderAsync();

        var response = await ConfirmAsync(order.PaymentId, "notif-order-paid-1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await WaitForNotificationAsync(testSender, nameof(PaymentConfirmed));
        await WaitUntilAsync(
            async () => await IsProcessedAsync(typeof(OrderPaid).FullName!),
            "OrderPaid outbox message was not processed.");
        await WaitUntilAsync(
            async () => await CountAuditLogsAsync(nameof(OrderPaid), order.OrderId) == 1,
            "OrderPaid audit log was not written.");

        Assert.False(testSender.Contains<OrderPaid>());
        Assert.DoesNotContain(store.GetAll(), notification => notification.EventType == nameof(OrderPaid));
        Assert.True(await CountAuditLogsAsync(nameof(OrderPaid), order.OrderId) == 1);
    }

    [Fact]
    public async Task AdminShipDeliverReturn_CreatesOrderNotifications()
    {
        var (sender, store) = Start();
        var testSender = Assert.IsType<TestNotificationSender>(sender);
        var order = await CreateOrderAsync();
        var confirm = await ConfirmAsync(order.PaymentId, "notif-ship-confirm-1");
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        await WaitForNotificationAsync(testSender, nameof(PaymentConfirmed));

        var ship = await SendAdminAsync(HttpMethod.Post, $"/api/admin/orders/{order.OrderId}/ship");
        Assert.Equal(HttpStatusCode.OK, ship.StatusCode);
        var shipped = await WaitForNotificationAsync(testSender, nameof(OrderShipped));
        Assert.Equal(SeedData.AliceEmail, shipped.Email);
        Assert.Equal(order.OrderId, shipped.Notification.EntityId);
        Assert.Equal(order.OrderId, shipped.Notification.OrderId);
        Assert.Equal(await GetUserIdAsync(SeedData.AliceEmail), shipped.Notification.UserId);

        var deliver = await SendAdminAsync(HttpMethod.Post, $"/api/admin/orders/{order.OrderId}/deliver");
        Assert.Equal(HttpStatusCode.OK, deliver.StatusCode);
        var delivered = await WaitForNotificationAsync(testSender, nameof(OrderDelivered));
        Assert.Equal(SeedData.AliceEmail, delivered.Email);
        Assert.Equal(order.OrderId, delivered.Notification.EntityId);
        Assert.Equal(order.OrderId, delivered.Notification.OrderId);
        Assert.Equal(await GetUserIdAsync(SeedData.AliceEmail), delivered.Notification.UserId);

        var returnResponse = await SendAdminAsync(HttpMethod.Post, $"/api/admin/orders/{order.OrderId}/return");
        Assert.Equal(HttpStatusCode.OK, returnResponse.StatusCode);
        var returned = await WaitForNotificationAsync(testSender, nameof(OrderReturned));
        Assert.Equal(SeedData.AliceEmail, returned.Email);
        Assert.Equal(order.OrderId, returned.Notification.EntityId);
        Assert.Equal(order.OrderId, returned.Notification.OrderId);
        Assert.Equal(await GetUserIdAsync(SeedData.AliceEmail), returned.Notification.UserId);

        var refunded = await WaitForNotificationAsync(testSender, nameof(PaymentRefunded));
        Assert.Equal(SeedData.AliceEmail, refunded.Email);
        Assert.Equal(order.PaymentId, refunded.Notification.EntityId);
        Assert.Equal(order.OrderId, refunded.Notification.OrderId);
        Assert.Equal(await GetUserIdAsync(SeedData.AliceEmail), refunded.Notification.UserId);
        Assert.Equal(order.TotalAmount, refunded.Notification.Amount);
    }

    [Fact]
    public async Task FullCancel_CreatesOrderCancelledNotification()
    {
        var (sender, store) = Start();
        var testSender = Assert.IsType<TestNotificationSender>(sender);
        var order = await CreateOrderAsync();

        var response = await CancelAsync(order.OrderId, order.OrderETag);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = await WaitForNotificationAsync(testSender, nameof(OrderCancelled));
        Assert.Equal(SeedData.AliceEmail, sent.Email);
        Assert.Equal(order.OrderId, sent.Notification.EntityId);
        Assert.Equal(order.OrderId, sent.Notification.OrderId);
        Assert.Equal(await GetUserIdAsync(SeedData.AliceEmail), sent.Notification.UserId);
        Assert.Contains("full cancellation: True", sent.Notification.Message);
    }

    [Fact]
    public async Task PartialCancel_CreatesOrderCancelledNotification()
    {
        var (sender, store) = Start();
        var testSender = Assert.IsType<TestNotificationSender>(sender);
        var order = await CreateOrderAsync(quantity: 2);
        var orderItemId = await GetOrderItemIdAsync(order.OrderId);

        var response = await CancelAsync(
            order.OrderId,
            order.OrderETag,
            new { items = new[] { new { orderItemId, quantity = 1 } } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = await WaitForNotificationAsync(testSender, nameof(OrderCancelled));
        Assert.Equal(SeedData.AliceEmail, sent.Email);
        Assert.Equal(order.OrderId, sent.Notification.EntityId);
        Assert.Equal(order.OrderId, sent.Notification.OrderId);
        Assert.Contains("full cancellation: False", sent.Notification.Message);
    }

    [Fact]
    public async Task AdminRefund_CreatesPaymentRefundedNotification()
    {
        var (sender, store) = Start();
        var testSender = Assert.IsType<TestNotificationSender>(sender);
        var order = await CreateOrderAsync();
        var confirm = await ConfirmAsync(order.PaymentId, "notif-refund-confirm-1");
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        await WaitForNotificationAsync(testSender, nameof(PaymentConfirmed));

        var response = await RefundAsync(order.PaymentId, 5m, "notif-refund-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = await WaitForNotificationAsync(testSender, nameof(PaymentRefunded));
        Assert.Equal(SeedData.AliceEmail, sent.Email);
        Assert.Equal(order.PaymentId, sent.Notification.EntityId);
        Assert.Equal(order.OrderId, sent.Notification.OrderId);
        Assert.Equal(await GetUserIdAsync(SeedData.AliceEmail), sent.Notification.UserId);
        Assert.Equal(5m, sent.Notification.Amount);
    }

    [Fact]
    public async Task AdminStockAndDeactivate_DoNotCreateNotifications()
    {
        var (sender, store) = Start();
        var testSender = Assert.IsType<TestNotificationSender>(sender);
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

        Assert.Equal(0, testSender.Count);
        Assert.Empty(store.GetAll());
    }

    [Fact]
    public async Task NotificationSubscriber_DeduplicatesRepeatedDelivery()
    {
        var (sender, store) = Start();
        var testSender = Assert.IsType<TestNotificationSender>(sender);
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

        Assert.Equal(2, testSender.Count);
        Assert.All(
            testSender.GetAll(),
            entry => Assert.Equal(SeedData.AliceEmail, entry.Email));

        var notification = Assert.Single(store.GetAll());
        Assert.Equal(nameof(OrderPlaced), notification.EventType);
        Assert.Equal(domainEvent.OrderId, notification.EntityId);
        Assert.Equal(domainEvent.OccurredAtUtc, notification.OccurredAtUtc);
    }

    [Fact]
    public async Task SubscriberFailure_RetriesNotificationAndStoresAfterSuccess()
    {
        var throwingSender = new ThrowingNotificationSender(failuresBeforeSuccess: 2);
        var (_, store) = Start(throwingSender);
        var order = await CreateOrderAsync();

        await WaitUntilAsync(
            async () => await IsProcessedAsync(typeof(OrderPlaced).FullName!),
            "OrderPlaced was not processed after notification retries.",
            timeoutMs: 30000);

        Assert.Equal(3, throwingSender.Attempts);

        var notification = Assert.Single(store.GetAll());
        Assert.Equal(nameof(OrderPlaced), notification.EventType);
        Assert.Equal(order.OrderId, notification.EntityId);

        await using var context = CreateContext();
        var message = await context.OutboxMessages
            .AsNoTracking()
            .SingleAsync(candidate => candidate.EventType == typeof(OrderPlaced).FullName);

        Assert.Equal(2, message.Attempts);
        Assert.NotNull(message.ProcessedAtUtc);
        Assert.Contains("Simulated notification sender failure", message.LastError);
    }

    [Fact]
    public async Task SubscriberFailure_ExhaustsRetriesAndDoesNotStoreNotification()
    {
        var throwingSender = new ThrowingNotificationSender(failuresBeforeSuccess: 100);
        var (_, store) = Start(throwingSender);
        await CreateOrderAsync();

        await WaitUntilAsync(
            () => Task.FromResult(throwingSender.Attempts >= 1),
            "Notification sender was not called.",
            timeoutMs: 15000);

        Assert.Empty(store.GetAll());

        await WaitUntilAsync(
            async () =>
            {
                await using var context = CreateContext();
                return await context.OutboxMessages
                    .AnyAsync(candidate =>
                        candidate.EventType == typeof(OrderPlaced).FullName
                        && candidate.Attempts >= 5);
            },
            "Notification was not marked failed after max attempts.",
            timeoutMs: 70000);

        Assert.Equal(5, throwingSender.Attempts);
        Assert.Empty(store.GetAll());

        await using var verification = CreateContext();
        var failed = await verification.OutboxMessages
            .AsNoTracking()
            .SingleAsync(candidate => candidate.EventType == typeof(OrderPlaced).FullName);

        Assert.Equal(5, failed.Attempts);
        Assert.NotNull(failed.ProcessedAtUtc);
        Assert.Contains("Simulated notification sender failure", failed.LastError);
    }

    private (INotificationSender Sender, TestNotificationStore Store) Start(
        INotificationSender? sender = null)
    {
        var store = new TestNotificationStore();
        var notificationSender = sender ?? new TestNotificationSender();

        _factory = new CustomWebApplicationFactory(
            _fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.RemoveAll<INotificationSender>();
                services.AddScoped<INotificationSender>(_ => notificationSender);

                services.RemoveAll<INotificationStore>();
                services.AddSingleton<INotificationStore>(store);
            },
            enableOutboxDispatcher: true);

        _client = _factory.CreateClient();

        return (notificationSender, store);
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

    private static async Task<SentNotification> WaitForNotificationAsync(
        TestNotificationSender sender,
        string eventType)
    {
        SentNotification? sent = null;

        await WaitUntilAsync(
            () =>
            {
                sent = sender.GetAll()
                    .Where(entry => entry.Notification.EventType == eventType)
                    .Select(entry => new SentNotification(entry.Email, entry.Notification))
                    .FirstOrDefault();
                return Task.FromResult(sent is not null);
            },
            $"Notification {eventType} was not sent.");

        return sent!;
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

    private sealed record SentNotification(string Email, Notification Notification);

    private sealed record OrderResponse(Guid Id, decimal TotalAmount, Guid? PaymentId);
}

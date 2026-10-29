using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PS.MiniEcommerce.Application.Abstractions.Outbox;
using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.Infrastructure.Persistence;
using PS.MiniEcommerce.Infrastructure.Serialization;
using PS.MiniEcommerce.IntegrationTests.Fixtures;
using Xunit.Sdk;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class OutboxDispatcherIntegrationTests : IAsyncLifetime
{
    private const string Alice = "alice";
    private const string Admin = "admin";
    private const string WirelessMouse = "Wireless Mouse";

    private readonly TestDatabaseFixture _fixture;

    private CustomWebApplicationFactory? _factory;
    private HttpClient? _client;

    public OutboxDispatcherIntegrationTests(TestDatabaseFixture fixture)
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
    public async Task Checkout_DispatcherDeliversOrderPlacedAndAuditIsNotDuplicated()
    {
        var capture = Start(outboxCapture: new OutboxCapture());

        var order = await CreateOrderAsync();

        await WaitUntilAsync(
            () => Task.FromResult(capture.Contains<OrderPlaced>()),
            "OrderPlaced was not delivered.");

        await WaitUntilAsync(
            () => Task.FromResult(capture.CountOf<OrderPlaced>() == 1),
            "OrderPlaced was delivered more than once.");

        await WaitUntilAsync(
            async () => await IsProcessedAsync(typeof(OrderPlaced).FullName!),
            "OrderPlaced outbox message was not processed.");

        await using var context = CreateContext();
        var auditCount = await context.AuditLogs
            .CountAsync(log => log.Action == "OrderPlaced" && log.EntityId == order.OrderId);

        Assert.Equal(1, auditCount);
    }

    [Fact]
    public async Task ConfirmAndRefund_DispatchPaymentEvents()
    {
        var capture = Start(outboxCapture: new OutboxCapture());

        var order = await CreateOrderAsync();

        var confirm = await SendAsync(
            HttpMethod.Post,
            $"/api/payments/{order.PaymentId}/confirm",
            Alice,
            idempotencyKey: "dispatcher-confirm-1");
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        var refund = await SendAsync(
            HttpMethod.Post,
            $"/api/payments/{order.PaymentId}/refund",
            Admin,
            new { amount = 5m, reason = "customer_request" },
            "dispatcher-refund-1");
        Assert.Equal(HttpStatusCode.OK, refund.StatusCode);

        await WaitUntilAsync(
            () => Task.FromResult(capture.Contains<PaymentConfirmed>()),
            "PaymentConfirmed was not delivered.");
        await WaitUntilAsync(
            () => Task.FromResult(capture.Contains<OrderPaid>()),
            "OrderPaid was not delivered.");
        await WaitUntilAsync(
            () => Task.FromResult(capture.Contains<PaymentRefunded>()),
            "PaymentRefunded was not delivered.");

        Assert.True(await IsProcessedAsync(typeof(PaymentConfirmed).FullName!));
        Assert.True(await IsProcessedAsync(typeof(OrderPaid).FullName!));
        Assert.True(await IsProcessedAsync(typeof(PaymentRefunded).FullName!));
    }

    [Fact]
    public async Task SubscriberFailure_RetriesUntilProcessed()
    {
        var capture = new OutboxCapture();
        var throwingSubscriber = new ThrowingOutboxSubscriber(failuresBeforeSuccess: 2);

        Start(capture, services => services.AddSingleton<IOutboxSubscriber>(throwingSubscriber));

        var order = await CreateOrderAsync();

        await WaitUntilAsync(
            () => Task.FromResult(capture.Contains<OrderPlaced>()),
            "OrderPlaced was not delivered after retries.",
            timeoutMs: 25000);

        await WaitUntilAsync(
            async () => await IsProcessedAsync(typeof(OrderPlaced).FullName!),
            "OrderPlaced was not marked processed after retries.",
            timeoutMs: 25000);

        await using var context = CreateContext();
        var message = await context.OutboxMessages
            .AsNoTracking()
            .SingleAsync(candidate => candidate.EventType == typeof(OrderPlaced).FullName);

        Assert.Equal(2, message.Attempts);
        Assert.NotNull(message.ProcessedAtUtc);
        Assert.NotNull(message.LastAttemptAtUtc);
        Assert.Contains("Simulated outbox subscriber failure", message.LastError);
        Assert.True(throwingSubscriber.DeliveryAttempts >= 3);
        Assert.Equal(1, await context.AuditLogs.CountAsync(
            log => log.Action == "OrderPlaced" && log.EntityId == order.OrderId));
    }

    [Fact]
    public async Task AttemptsAtMax_MarksMessageProcessedWithoutFurtherDelivery()
    {
        var throwingSubscriber = new ThrowingOutboxSubscriber(failuresBeforeSuccess: 100);

        Start(
            outboxCapture: null,
            services => services.AddSingleton<IOutboxSubscriber>(throwingSubscriber));

        await using (var context = CreateContext())
        {
            var message = OutboxMessage.Create(
                typeof(OrderPlaced).FullName!,
                CreateOrderPlacedPayload(),
                DateTime.UtcNow);

            for (var attempt = 0; attempt < 4; attempt++)
            {
                message.MarkFailedAttempt("previous failure", DateTime.UtcNow.AddSeconds(-20), 5);
            }

            context.OutboxMessages.Add(message);
            await context.SaveChangesAsync();
        }

        await WaitUntilAsync(
            async () =>
            {
                await using var context = CreateContext();
                return await context.OutboxMessages
                    .AnyAsync(candidate =>
                        candidate.EventType == typeof(OrderPlaced).FullName
                        && candidate.Attempts >= 5);
            },
            "Message was not marked as failed after reaching max attempts.",
            timeoutMs: 15000);

        await using var verification = CreateContext();
        var failed = await verification.OutboxMessages
            .AsNoTracking()
            .SingleAsync(candidate => candidate.EventType == typeof(OrderPlaced).FullName);

        Assert.Equal(5, failed.Attempts);
        Assert.NotNull(failed.ProcessedAtUtc);
        Assert.Contains("Simulated outbox subscriber failure", failed.LastError);
    }

    [Fact]
    public async Task UnknownEventType_MarksFailedAttemptAndKeepsMessagePending()
    {
        var capture = Start(outboxCapture: new OutboxCapture());

        await using (var context = CreateContext())
        {
            var message = OutboxMessage.Create(
                "Unknown.EventType",
                "{}",
                DateTime.UtcNow);

            context.OutboxMessages.Add(message);
            await context.SaveChangesAsync();
        }

        await WaitUntilAsync(
            async () =>
            {
                await using var context = CreateContext();
                return await context.OutboxMessages
                    .AnyAsync(candidate =>
                        candidate.EventType == "Unknown.EventType" && candidate.Attempts >= 1);
            },
            "Unknown event type was not recorded as a failed attempt.");

        await using var verification = CreateContext();
        var failed = await verification.OutboxMessages
            .AsNoTracking()
            .SingleAsync(candidate => candidate.EventType == "Unknown.EventType");

        Assert.Null(failed.ProcessedAtUtc);
        Assert.Equal(1, failed.Attempts);
        Assert.Contains("Unknown event type", failed.LastError);
        Assert.Equal(0, capture.Count);
    }

    [Fact]
    public async Task CorruptedPayload_MarksFailedAttempt()
    {
        var capture = Start(outboxCapture: new OutboxCapture());

        await using (var context = CreateContext())
        {
            var message = OutboxMessage.Create(
                typeof(OrderPlaced).FullName!,
                "{not-json",
                DateTime.UtcNow);

            context.OutboxMessages.Add(message);
            await context.SaveChangesAsync();
        }

        await WaitUntilAsync(
            async () =>
            {
                await using var context = CreateContext();
                return await context.OutboxMessages
                    .AnyAsync(candidate =>
                        candidate.EventType == typeof(OrderPlaced).FullName
                        && candidate.Attempts >= 1);
            },
            "Corrupted payload was not recorded as a failed attempt.");

        await using var verification = CreateContext();
        var failed = await verification.OutboxMessages
            .AsNoTracking()
            .SingleAsync(candidate => candidate.EventType == typeof(OrderPlaced).FullName);

        Assert.Null(failed.ProcessedAtUtc);
        Assert.Equal(1, failed.Attempts);
        Assert.NotNull(failed.LastError);
        Assert.Equal(0, capture.Count);
    }

    private OutboxCapture Start(
        OutboxCapture? outboxCapture,
        Action<IServiceCollection>? configureTestServices = null)
    {
        var capture = outboxCapture ?? new OutboxCapture();

        _factory = new CustomWebApplicationFactory(
            _fixture.ConnectionString,
            configureTestServices: configureTestServices,
            outboxCapture: outboxCapture,
            enableOutboxDispatcher: true);

        _client = _factory.CreateClient();

        return capture;
    }

    private async Task<CreatedOrder> CreateOrderAsync()
    {
        var productId = await GetActiveProductIdAsync();

        var cartRequest = new HttpRequestMessage(HttpMethod.Get, "/api/cart");
        cartRequest.Headers.Add("X-Test-User", Alice);
        var cartResponse = await _client!.SendAsync(cartRequest);
        cartResponse.EnsureSuccessStatusCode();

        var addRequest = new HttpRequestMessage(HttpMethod.Post, "/api/cart/items")
        {
            Content = JsonContent.Create(new { productId, quantity = 1 })
        };
        addRequest.Headers.Add("X-Test-User", Alice);
        addRequest.Headers.Add("If-Match", cartResponse.Headers.ETag!.Tag);
        var addResponse = await _client.SendAsync(addRequest);
        addResponse.EnsureSuccessStatusCode();

        var orderRequest = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        orderRequest.Headers.Add("X-Test-User", Alice);
        orderRequest.Headers.Add("Idempotency-Key", $"dispatcher-{Guid.NewGuid():N}");
        orderRequest.Headers.Add("If-Match", addResponse.Headers.ETag!.Tag);
        var orderResponse = await _client.SendAsync(orderRequest);
        orderResponse.EnsureSuccessStatusCode();

        var order = await orderResponse.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(order);
        Assert.NotNull(order.PaymentId);

        return new CreatedOrder(order.Id, order.PaymentId.Value);
    }

    private Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        string user,
        object? body = null,
        string? idempotencyKey = null)
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

        return _client!.SendAsync(request);
    }

    private async Task<bool> IsProcessedAsync(string eventType)
    {
        await using var context = CreateContext();
        return await context.OutboxMessages
            .AsNoTracking()
            .AnyAsync(message =>
                message.EventType == eventType && message.ProcessedAtUtc != null);
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

    private static string CreateOrderPlacedPayload()
    {
        var domainEvent = new OrderPlaced(
            Guid.NewGuid(),
            Guid.NewGuid(),
            PS.MiniEcommerce.Domain.Money.FromDecimal(19.99m),
            1,
            DateTime.UtcNow);

        return JsonSerializer.Serialize(
            domainEvent,
            domainEvent.GetType(),
            OutboxJsonOptions.Instance);
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

    private sealed record CreatedOrder(Guid OrderId, Guid PaymentId);

    private sealed record OrderResponse(Guid Id, Guid? PaymentId);
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.Infrastructure.Persistence;
using PS.MiniEcommerce.Infrastructure.Seed;
using PS.MiniEcommerce.IntegrationTests.Fixtures;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class AuditIntegrationTests : IAsyncLifetime
{
    private const string Alice = "alice";
    private const string WirelessMouse = "Wireless Mouse";

    private readonly TestDatabaseFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuditIntegrationTests(TestDatabaseFixture fixture)
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
    public async Task Checkout_WritesOrderPlacedAuditLog()
    {
        var (orderId, _) = await CreateOrderAsync(_client, "audit-checkout-1", quantity: 2);
        var actorUserId = await GetUserIdAsync(SeedData.AliceEmail);

        var auditLog = await GetAuditLogAsync("OrderPlaced");

        Assert.Equal("OrderPlaced", auditLog.Action);
        Assert.Equal("Order", auditLog.EntityType);
        Assert.Equal(orderId, auditLog.EntityId);
        Assert.Equal(actorUserId, auditLog.ActorUserId);
        Assert.Null(auditLog.CorrelationId);

        using var payload = JsonDocument.Parse(auditLog.Payload!);
        Assert.Equal(39.98m, payload.RootElement.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(1, payload.RootElement.GetProperty("itemCount").GetInt32());
    }

    [Fact]
    public async Task FullCancel_WritesOrderCancelledAuditLogWithFullCancellation()
    {
        var (orderId, orderETag) = await CreateOrderAsync(_client, "audit-cancel-full-1", quantity: 2);
        var actorUserId = await GetUserIdAsync(SeedData.AliceEmail);

        var response = await CancelAsync(_client, orderId, orderETag);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auditLog = await GetAuditLogAsync("OrderCancelled");
        Assert.Equal(orderId, auditLog.EntityId);
        Assert.Equal(actorUserId, auditLog.ActorUserId);

        using var payload = JsonDocument.Parse(auditLog.Payload!);
        Assert.True(payload.RootElement.GetProperty("isFullCancellation").GetBoolean());
        var item = Assert.Single(payload.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(2, item.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task PartialCancel_WritesOnlyNewlyCancelledItems()
    {
        var (orderId, orderETag) = await CreateOrderAsync(_client, "audit-cancel-partial-1", quantity: 2);
        var orderItemId = await GetOrderItemIdAsync(orderId);

        var response = await CancelAsync(
            _client,
            orderId,
            orderETag,
            new { items = new[] { new { orderItemId, quantity = 1 } } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auditLog = await GetAuditLogAsync("OrderCancelled");

        using var payload = JsonDocument.Parse(auditLog.Payload!);
        Assert.False(payload.RootElement.GetProperty("isFullCancellation").GetBoolean());
        var item = Assert.Single(payload.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(orderItemId, item.GetProperty("orderItemId").GetGuid());
        Assert.Equal(1, item.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Seed_DoesNotWriteAuditLogs()
    {
        Assert.Equal(0, await GetAuditCountAsync());
    }

    [Fact]
    public async Task AuditFailure_DoesNotBreakCheckoutAndDoesNotWriteAuditLog()
    {
        using var factory = new CustomWebApplicationFactory(
            _fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.RemoveAll<IAuditLogRepository>();
                services.AddScoped<IAuditLogRepository>(_ => new ThrowingAuditLogRepository());
            });
        using var client = factory.CreateClient();

        var (orderId, _) = await CreateOrderAsync(client, "audit-failure-1", quantity: 1);

        Assert.True(await OrderExistsAsync(orderId));
        Assert.Equal(0, await GetAuditCountAsync());
    }

    [Fact]
    public async Task AuditLogs_AreAppendOnly()
    {
        var auditLogId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var actorUserId = await GetUserIdAsync(SeedData.AliceEmail);
        var occurredAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await using (var context = CreateContext())
        {
            context.AuditLogs.Add(new AuditLog(
                auditLogId,
                occurredAtUtc,
                actorUserId,
                "OrderPlaced",
                "Order",
                entityId,
                "{}",
                null));

            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var exception = await Record.ExceptionAsync(
                () => context.AuditLogs
                    .Where(log => log.Id == auditLogId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(log => log.Action, "Changed")));

            AssertTriggerFailure(exception);
        }

        await using (var context = CreateContext())
        {
            var exception = await Record.ExceptionAsync(
                () => context.AuditLogs
                    .Where(log => log.Id == auditLogId)
                    .ExecuteDeleteAsync());

            AssertTriggerFailure(exception);
        }

        await using (var context = CreateContext())
        {
            var auditLog = await context.AuditLogs
                .AsNoTracking()
                .SingleAsync(log => log.Id == auditLogId);

            Assert.Equal("OrderPlaced", auditLog.Action);
        }
    }

    [Fact]
    public async Task AuditLog_AllowsNullActorUserId()
    {
        var auditLogId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            context.AuditLogs.Add(new AuditLog(
                auditLogId,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                actorUserId: null,
                "SystemAction",
                "System",
                Guid.NewGuid(),
                payload: null,
                correlationId: null));

            await context.SaveChangesAsync();
        }

        await using (var context = CreateContext())
        {
            var auditLog = await context.AuditLogs
                .AsNoTracking()
                .SingleAsync(log => log.Id == auditLogId);

            Assert.Null(auditLog.ActorUserId);
        }
    }

    private async Task<(Guid OrderId, string OrderETag)> CreateOrderAsync(
        HttpClient client,
        string idempotencyKey,
        int quantity)
    {
        var productId = await GetActiveProductIdAsync();
        var cartETag = await AddCartItemAsync(client, productId, quantity);
        var response = await client.SendAsync(CreateCheckoutRequest(idempotencyKey, cartETag));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var orderId = Guid.Parse(response.Headers.Location!.ToString().Split('/').Last());
        return (orderId, response.Headers.ETag!.Tag);
    }

    private async Task<string> AddCartItemAsync(HttpClient client, Guid productId, int quantity)
    {
        var cartRequest = new HttpRequestMessage(HttpMethod.Get, "/api/cart");
        cartRequest.Headers.Add("X-Test-User", Alice);
        var cartResponse = await client.SendAsync(cartRequest);
        cartResponse.EnsureSuccessStatusCode();

        var addRequest = new HttpRequestMessage(HttpMethod.Post, "/api/cart/items")
        {
            Content = JsonContent.Create(new { productId, quantity })
        };
        addRequest.Headers.Add("X-Test-User", Alice);
        addRequest.Headers.Add("If-Match", cartResponse.Headers.ETag!.Tag);
        var addResponse = await client.SendAsync(addRequest);
        addResponse.EnsureSuccessStatusCode();

        return addResponse.Headers.ETag!.Tag;
    }

    private static HttpRequestMessage CreateCheckoutRequest(string idempotencyKey, string ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.Add("X-Test-User", Alice);
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        request.Headers.Add("If-Match", ifMatch);
        return request;
    }

    private static async Task<HttpResponseMessage> CancelAsync(
        HttpClient client,
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

        return await client.SendAsync(request);
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
            .SingleAsync();
    }

    private async Task<AuditLog> GetAuditLogAsync(string action)
    {
        await using var context = CreateContext();
        return await context.AuditLogs
            .AsNoTracking()
            .SingleAsync(log => log.Action == action);
    }

    private async Task<int> GetAuditCountAsync()
    {
        await using var context = CreateContext();
        return await context.AuditLogs.CountAsync();
    }

    private async Task<bool> OrderExistsAsync(Guid orderId)
    {
        await using var context = CreateContext();
        return await context.Orders
            .AsNoTracking()
            .AnyAsync(order => order.Id == orderId);
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options);

    private static void AssertTriggerFailure(Exception? exception)
    {
        Assert.NotNull(exception);

        var sqlException = exception as SqlException
            ?? exception.InnerException as SqlException;

        Assert.NotNull(sqlException);
        Assert.Equal(51000, sqlException.Number);
    }

    private sealed class ThrowingAuditLogRepository : IAuditLogRepository
    {
        public Task AddAsync(AuditLogEntry entry, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("Simulated audit failure."));
    }
}

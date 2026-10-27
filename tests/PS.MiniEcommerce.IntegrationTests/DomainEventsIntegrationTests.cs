using System.Net;
using System.Net.Http.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PS.MiniEcommerce.Application.Events;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.IntegrationTests.Fixtures;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class DomainEventsIntegrationTests : IAsyncLifetime
{
    private const string Alice = "alice";
    private const string WirelessMouse = "Wireless Mouse";

    private readonly TestDatabaseFixture _fixture;
    private readonly NotificationCapture _capture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public DomainEventsIntegrationTests(TestDatabaseFixture fixture)
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
    public async Task Checkout_PublishesOrderPlacedAfterCommit()
    {
        var (orderId, _, _) = await CreateOrderAsync(_client, "events-checkout-1", quantity: 1);

        var orderPlaced = Assert.Single(_capture.Events.OfType<OrderPlaced>());
        Assert.Equal(orderId, orderPlaced.OrderId);
        Assert.True(_capture.OrderPlacedWasVisibleAfterCommit);
        Assert.True(await OrderExistsAsync(orderId));
    }

    [Fact]
    public async Task CheckoutReplay_DoesNotPublishOrderPlacedAgain()
    {
        var (_, cartETag, _) = await CreateOrderAsync(_client, "events-replay-1", quantity: 1);
        _capture.Clear();

        var replay = await _client.SendAsync(CreateCheckoutRequest("events-replay-1", cartETag));

        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Empty(_capture.Events);
        Assert.Equal(1, await GetOrdersCountAsync());
    }

    [Fact]
    public async Task CheckoutRollback_DoesNotPublishOrderPlaced()
    {
        var capture = new NotificationCapture();
        using var factory = new CustomWebApplicationFactory(
            _fixture.ConnectionString,
            saveChangesInterceptor: new FailOrderSaveInterceptor(),
            notificationCapture: capture);
        using var client = factory.CreateClient();

        var productId = await GetActiveProductIdAsync();
        var etag = await AddCartItemAsync(client, productId, quantity: 1);

        var response = await client.SendAsync(CreateCheckoutRequest("events-rollback-1", etag));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(capture.Events);
        Assert.Equal(0, await GetOrdersCountAsync());
    }

    [Fact]
    public async Task FullCancel_PublishesOrderCancelledAfterCommit()
    {
        var (orderId, _, orderETag) = await CreateOrderAsync(_client, "events-cancel-full-1", quantity: 2);
        _capture.Clear();

        var response = await CancelAsync(_client, orderId, orderETag);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var orderCancelled = Assert.Single(_capture.Events.OfType<OrderCancelled>());
        Assert.Equal(orderId, orderCancelled.OrderId);
        Assert.True(orderCancelled.IsFullCancellation);
        Assert.True(_capture.OrderCancelledWasVisibleAfterCommit);
    }

    [Fact]
    public async Task PartialCancel_PublishesOrderCancelledWithIsFullCancellationFalse()
    {
        var (orderId, _, orderETag) = await CreateOrderAsync(_client, "events-cancel-partial-1", quantity: 2);
        var orderItemId = await GetOrderItemIdAsync(orderId);
        _capture.Clear();

        var response = await CancelAsync(
            _client,
            orderId,
            orderETag,
            new { items = new[] { new { orderItemId, quantity = 1 } } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var orderCancelled = Assert.Single(_capture.Events.OfType<OrderCancelled>());
        Assert.Equal(orderId, orderCancelled.OrderId);
        Assert.False(orderCancelled.IsFullCancellation);
        Assert.True(_capture.OrderCancelledWasVisibleAfterCommit);
    }

    [Fact]
    public async Task FullCancelViaItems_PublishesFullOrderCancelled()
    {
        var (orderId, _, orderETag) = await CreateOrderAsync(_client, "events-cancel-items-1", quantity: 2);
        var orderItemId = await GetOrderItemIdAsync(orderId);
        _capture.Clear();

        var response = await CancelAsync(
            _client,
            orderId,
            orderETag,
            new { items = new[] { new { orderItemId, quantity = 2 } } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var orderCancelled = Assert.Single(_capture.Events.OfType<OrderCancelled>());
        Assert.True(orderCancelled.IsFullCancellation);
    }

    [Fact]
    public async Task NotificationHandlerFailure_DoesNotBreakCheckout()
    {
        var capture = new NotificationCapture();
        using var factory = new CustomWebApplicationFactory(
            _fixture.ConnectionString,
            notificationCapture: capture,
            configureTestServices: services => services.AddSingleton<
                INotificationHandler<DomainEventNotification<object>>,
                ThrowingNotificationHandler<object>>());
        using var client = factory.CreateClient();

        var productId = await GetActiveProductIdAsync();
        var etag = await AddCartItemAsync(client, productId, quantity: 1);

        var response = await client.SendAsync(CreateCheckoutRequest("events-throwing-1", etag));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var orderId = Guid.Parse(response.Headers.Location!.ToString().Split('/').Last());
        Assert.True(await OrderExistsAsync(orderId));
    }

    [Fact]
    public void Seeder_DoesNotPublishProductDeactivated()
    {
        Assert.DoesNotContain(_capture.Events, domainEvent => domainEvent is ProductDeactivated);
    }

    private async Task<(Guid OrderId, string CartETag, string OrderETag)> CreateOrderAsync(
        HttpClient client,
        string idempotencyKey,
        int quantity)
    {
        var productId = await GetActiveProductIdAsync();
        var cartETag = await AddCartItemAsync(client, productId, quantity);
        var response = await client.SendAsync(CreateCheckoutRequest(idempotencyKey, cartETag));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var orderId = Guid.Parse(response.Headers.Location!.ToString().Split('/').Last());
        return (orderId, cartETag, response.Headers.ETag!.Tag);
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

    private static HttpRequestMessage CreateCheckoutRequest(string? idempotencyKey, string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.Add("X-Test-User", Alice);

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (ifMatch is not null)
        {
            request.Headers.Add("If-Match", ifMatch);
        }

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

    private async Task<bool> OrderExistsAsync(Guid orderId)
    {
        await using var context = CreateContext();
        return await context.Orders
            .AsNoTracking()
            .AnyAsync(order => order.Id == orderId);
    }

    private async Task<int> GetOrdersCountAsync()
    {
        await using var context = CreateContext();
        return await context.Orders.CountAsync();
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options);

    private sealed class FailOrderSaveInterceptor : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var hasNewOrder = eventData.Context?.ChangeTracker
                .Entries<Order>()
                .Any(entry => entry.State == EntityState.Added) == true;

            if (hasNewOrder)
            {
                throw new DbUpdateException("Simulated failure while saving the order.");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class ThrowingNotificationHandler<T> : INotificationHandler<DomainEventNotification<T>>
        where T : class
    {
        public Task Handle(DomainEventNotification<T> notification, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Simulated notification handler failure.");
        }
    }
}

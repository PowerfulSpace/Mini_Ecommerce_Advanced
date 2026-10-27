using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.IntegrationTests.Fixtures;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class OrderCancelEndpointTests : IAsyncLifetime
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const string WirelessMouse = "Wireless Mouse";

    private readonly TestDatabaseFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public OrderCancelEndpointTests(TestDatabaseFixture fixture)
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
    public async Task CancelOrder_WhenEligible_ReturnsCancelledOrder()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 2);
        var stockAfterCheckout = await GetStockQuantityAsync(WirelessMouse);

        var response = await CancelAsync(Alice, orderId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderDetailResponse>();
        Assert.NotNull(order);
        Assert.Equal(orderId, order.Id);
        Assert.Equal("Cancelled", order.Status);

        Assert.Equal(OrderStatus.Cancelled, await GetOrderStatusAsync(orderId));
        Assert.Equal(stockAfterCheckout + 2, await GetStockQuantityAsync(WirelessMouse));
    }

    [Fact]
    public async Task CancelOrder_WhenAlreadyCancelled_Returns409WithOrderAlreadyCancelled()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 2);
        var first = await CancelAsync(Alice, orderId);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var stockAfterCancel = await GetStockQuantityAsync(WirelessMouse);

        var second = await CancelAsync(Alice, orderId);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var error = await second.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("order_already_cancelled", error.Code);

        Assert.Equal(stockAfterCancel, await GetStockQuantityAsync(WirelessMouse));
    }

    [Fact]
    public async Task CancelOrder_WhenOrderBelongsToAnotherUser_Returns404()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);

        var response = await CancelAsync(Bob, orderId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("order_not_found", error.Code);
    }

    [Fact]
    public async Task CancelOrder_WhenOrderDoesNotExist_Returns404()
    {
        var response = await CancelAsync(Alice, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("order_not_found", error.Code);
    }

    [Fact]
    public async Task CancelOrder_Unauthenticated_Returns401()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/cancel");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("unauthenticated", error.Code);
    }

    [Fact]
    public async Task CancelOrder_ConcurrentCancellations_OneSucceedsOneConflicts()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 2);
        var stockAfterCheckout = await GetStockQuantityAsync(WirelessMouse);

        var tasks = new[]
        {
            Task.Run(() => CancelAsync(Alice, orderId)),
            Task.Run(() => CancelAsync(Alice, orderId))
        };

        var responses = await Task.WhenAll(tasks);
        var statuses = responses.Select(response => response.StatusCode).ToList();

        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.OK));
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Conflict));

        Assert.Equal(OrderStatus.Cancelled, await GetOrderStatusAsync(orderId));
        Assert.Equal(stockAfterCheckout + 2, await GetStockQuantityAsync(WirelessMouse));
    }

    [Fact]
    public async Task CancelOrder_WhenPaid_ReturnsCancelledAndDoesNotRestoreStock()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 2);
        var stockAfterCheckout = await GetStockQuantityAsync(WirelessMouse);
        await SetStatusAsync(orderId, OrderStatus.Paid, Guid.NewGuid());

        var response = await CancelAsync(Alice, orderId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderDetailResponse>();
        Assert.NotNull(order);
        Assert.Equal(orderId, order.Id);
        Assert.Equal("Cancelled", order.Status);

        Assert.Equal(OrderStatus.Cancelled, await GetOrderStatusAsync(orderId));
        Assert.Equal(stockAfterCheckout, await GetStockQuantityAsync(WirelessMouse));
    }

    [Theory]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Returned)]
    public async Task CancelOrder_WhenStatusDoesNotAllowCancellation_Returns409InvalidTransition(
        OrderStatus status)
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);
        var stockBeforeCancel = await GetStockQuantityAsync(WirelessMouse);
        await SetStatusAsync(orderId, status);

        var response = await CancelAsync(Alice, orderId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("invalid_order_transition", error.Code);

        Assert.Equal(status, await GetOrderStatusAsync(orderId));
        Assert.Equal(stockBeforeCancel, await GetStockQuantityAsync(WirelessMouse));
    }

    [Fact]
    public async Task PaymentId_IsNullableAndPersists()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);

        await using (var context = CreateContext())
        {
            var initialPaymentId = await context.Orders
                .AsNoTracking()
                .Where(order => order.Id == orderId)
                .Select(order => order.PaymentId)
                .FirstAsync();

            Assert.Null(initialPaymentId);

            var paymentId = Guid.NewGuid();
            var affected = await context.Orders
                .Where(order => order.Id == orderId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(order => order.PaymentId, paymentId));

            Assert.Equal(1, affected);
        }

        await using (var context = CreateContext())
        {
            var storedPaymentId = await context.Orders
                .AsNoTracking()
                .Where(order => order.Id == orderId)
                .Select(order => order.PaymentId)
                .FirstAsync();

            Assert.NotNull(storedPaymentId);
            Assert.NotEqual(Guid.Empty, storedPaymentId.Value);
        }
    }

    [Fact]
    public async Task StatusCheck_AcceptsSixStatusesAndRejectsUnknown()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);

        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            await SetStatusAsync(orderId, status);

            Assert.Equal(status, await GetOrderStatusAsync(orderId));
        }

        await using (var context = CreateContext())
        {
            var exception = await Record.ExceptionAsync(() =>
                context.Database.ExecuteSqlRawAsync(
                    "UPDATE Orders SET Status = {0} WHERE Id = {1}",
                    "Unknown",
                    orderId));

            Assert.NotNull(exception);
            Assert.Contains("CK_Orders_Status_Valid", exception.Message);
        }

        Assert.Equal(OrderStatus.Returned, await GetOrderStatusAsync(orderId));
    }

    private Task<HttpResponseMessage> CancelAsync(string testUser, Guid orderId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/cancel");
        request.Headers.Add("X-Test-User", testUser);
        return _client.SendAsync(request);
    }

    private async Task<Guid> CreateOrderAsync(string testUser, string productName, int quantity)
    {
        var productId = await GetActiveProductIdAsync(productName);

        var cartRequest = new HttpRequestMessage(HttpMethod.Get, "/api/cart");
        cartRequest.Headers.Add("X-Test-User", testUser);
        var cartResponse = await _client.SendAsync(cartRequest);
        cartResponse.EnsureSuccessStatusCode();
        var etag = cartResponse.Headers.ETag!.Tag;

        var addRequest = new HttpRequestMessage(HttpMethod.Post, "/api/cart/items")
        {
            Content = JsonContent.Create(new { productId, quantity })
        };
        addRequest.Headers.Add("X-Test-User", testUser);
        addRequest.Headers.Add("If-Match", etag);
        var addResponse = await _client.SendAsync(addRequest);
        addResponse.EnsureSuccessStatusCode();
        var newEtag = addResponse.Headers.ETag!.Tag;

        var orderRequest = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        orderRequest.Headers.Add("X-Test-User", testUser);
        orderRequest.Headers.Add("Idempotency-Key", $"test-order-{Guid.NewGuid():N}".Substring(0, 32));
        orderRequest.Headers.Add("If-Match", newEtag);
        var orderResponse = await _client.SendAsync(orderRequest);
        orderResponse.EnsureSuccessStatusCode();

        var location = orderResponse.Headers.Location!.ToString();
        return Guid.Parse(location.Split('/').Last());
    }

    private async Task<Guid> GetActiveProductIdAsync(string name)
    {
        await using var context = CreateContext();
        return await context.Products
            .Where(product => product.Name == name && product.IsActive)
            .Select(product => product.Id)
            .FirstAsync();
    }

    private async Task<int> GetStockQuantityAsync(string name)
    {
        await using var context = CreateContext();
        return await context.Products
            .AsNoTracking()
            .Where(product => product.Name == name)
            .Select(product => product.StockQuantity)
            .FirstAsync();
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

    private async Task SetStatusAsync(Guid orderId, OrderStatus status, Guid? paymentId = null)
    {
        await using var context = CreateContext();
        var affected = await context.Orders
            .Where(order => order.Id == orderId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(order => order.Status, status)
                    .SetProperty(order => order.PaymentId, paymentId));

        Assert.Equal(1, affected);
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options);

    private sealed record OrderDetailResponse(Guid Id, string Status, DateTime CreatedAtUtc, decimal TotalAmount, List<OrderItemResponse> Items);

    private sealed record OrderItemResponse(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

    private sealed record ErrorResponse(string Code);
}

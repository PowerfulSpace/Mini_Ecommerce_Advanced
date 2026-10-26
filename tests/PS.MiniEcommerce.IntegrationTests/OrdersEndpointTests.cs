using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.IntegrationTests.Fixtures;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class OrdersEndpointTests : IAsyncLifetime
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const string WirelessMouse = "Wireless Mouse";

    private readonly TestDatabaseFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public OrdersEndpointTests(TestDatabaseFixture fixture)
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
    public async Task GetOrders_WhenNoOrders_ReturnsEmptyList()
    {
        var response = await GetOrdersAsync(Bob);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<OrdersPageResponse>();
        Assert.NotNull(page);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task GetOrders_WhenOneOrder_ReturnsSummary()
    {
        await CreateOrderAsync(Alice, WirelessMouse, 1);

        var response = await GetOrdersAsync(Alice);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);

        var items = document.RootElement.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(1, document.RootElement.GetProperty("totalCount").GetInt32());

        var propertyNames = items[0].EnumerateObject().Select(p => p.Name).OrderBy(name => name).ToArray();
        Assert.Equal(new[] { "createdAtUtc", "id", "status", "totalAmount" }, propertyNames);
    }

    [Fact]
    public async Task GetOrders_WhenMultipleOrders_ReturnsNewestFirst()
    {
        await CreateOrderAsync(Alice, WirelessMouse, 1);
        await CreateOrderAsync(Alice, WirelessMouse, 1);

        var response = await GetOrdersAsync(Alice);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<OrdersPageResponse>();
        Assert.NotNull(page);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task GetOrders_DoesNotReturnOtherUsersOrders()
    {
        await CreateOrderAsync(Alice, WirelessMouse, 1);

        var response = await GetOrdersAsync(Bob);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<OrdersPageResponse>();
        Assert.NotNull(page);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task GetOrders_WithPagination_ReturnsRequestedPage()
    {
        await CreateOrderAsync(Alice, WirelessMouse, 1);
        await CreateOrderAsync(Alice, WirelessMouse, 1);

        var response = await GetOrdersAsync(Alice, "?page=1&pageSize=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<OrdersPageResponse>();
        Assert.NotNull(page);
        Assert.Single(page.Items);
        Assert.Equal(1, page.PageSize);
        Assert.Equal(2, page.TotalCount);
    }

    [Fact]
    public async Task GetOrders_WithPageSizeAboveMaximum_Returns400()
    {
        var response = await GetOrdersAsync(Alice, "?pageSize=101");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("validation_error", error.Code);
    }

    [Fact]
    public async Task GetOrders_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync("/api/orders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("unauthenticated", error.Code);
    }

    [Fact]
    public async Task GetOrderById_WhenOrderExists_ReturnsOrderDto()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 2);

        var response = await GetOrderByIdAsync(Alice, orderId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderDetailResponse>();
        Assert.NotNull(order);
        Assert.Equal(orderId, order.Id);
        Assert.Equal("Created", order.Status);

        var item = Assert.Single(order.Items);
        Assert.Equal(WirelessMouse, item.ProductName);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(item.UnitPrice * item.Quantity, item.LineTotal);
    }

    [Fact]
    public async Task GetOrderById_WhenOrderBelongsToAnotherUser_Returns404()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);

        var response = await GetOrderByIdAsync(Bob, orderId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("order_not_found", error.Code);
    }

    [Fact]
    public async Task GetOrderById_WhenOrderDoesNotExist_Returns404()
    {
        var response = await GetOrderByIdAsync(Alice, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("order_not_found", error.Code);
    }

    [Fact]
    public async Task GetOrderById_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync($"/api/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("unauthenticated", error.Code);
    }

    private Task<HttpResponseMessage> GetOrdersAsync(string testUser, string query = "")
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/orders{query}");
        request.Headers.Add("X-Test-User", testUser);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> GetOrderByIdAsync(string testUser, Guid orderId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{orderId}");
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

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options);

    private sealed record OrdersPageResponse(List<OrderSummaryResponse> Items, int Page, int PageSize, int TotalCount);

    private sealed record OrderSummaryResponse(Guid Id, string Status, DateTime CreatedAtUtc, decimal TotalAmount);

    private sealed record OrderDetailResponse(Guid Id, string Status, DateTime CreatedAtUtc, decimal TotalAmount, List<OrderItemResponse> Items);

    private sealed record OrderItemResponse(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

    private sealed record ErrorResponse(string Code);
}

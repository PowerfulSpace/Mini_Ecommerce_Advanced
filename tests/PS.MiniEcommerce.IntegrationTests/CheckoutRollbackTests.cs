using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.Infrastructure.Persistence;
using PS.MiniEcommerce.Infrastructure.Seed;
using PS.MiniEcommerce.IntegrationTests.Fixtures;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class CheckoutRollbackTests : IAsyncLifetime
{
    private const string Alice = "alice";
    private const string WirelessMouse = "Wireless Mouse";

    private readonly TestDatabaseFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CheckoutRollbackTests(TestDatabaseFixture fixture)
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
    public async Task PostOrders_WithInsufficientStockAtCheckout_RollsBackEverything()
    {
        var productId = await GetActiveProductIdAsync(WirelessMouse);
        var etag = await AddItemToAliceCartAsync(productId, 5);

        await ExecuteSqlRawAsync(
            "UPDATE Products SET StockQuantity = {0} WHERE Id = {1}",
            3,
            productId);

        var response = await _client.SendAsync(CreateCheckoutRequest("checkout-insufficient-1", etag));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("insufficient_stock", error.Code);

        Assert.Equal(0, await GetOrdersCountAsync());
        Assert.Equal(3, await GetStockQuantityAsync(WirelessMouse));
        Assert.Equal(5, await GetAliceCartItemQuantityAsync(productId));

        var attempt = await GetCheckoutAttemptAsync("checkout-insufficient-1");
        Assert.NotNull(attempt);
        Assert.Equal(CheckoutAttemptStatus.Completed, attempt.Status);
        Assert.Equal(409, attempt.ResponseStatusCode);
    }

    [Fact]
    public async Task PostOrders_WithInactiveProductAtCheckout_RollsBackEverything()
    {
        var productId = await GetActiveProductIdAsync(WirelessMouse);
        var etag = await AddItemToAliceCartAsync(productId, 2);

        await ExecuteSqlRawAsync(
            "UPDATE Products SET IsActive = 0 WHERE Id = {0}",
            productId);

        var response = await _client.SendAsync(CreateCheckoutRequest("checkout-inactive-1", etag));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("product_unavailable", error.Code);

        Assert.Equal(0, await GetOrdersCountAsync());
        Assert.Equal(100, await GetStockQuantityAsync(WirelessMouse));
        Assert.Equal(2, await GetAliceCartItemQuantityAsync(productId));

        var attempt = await GetCheckoutAttemptAsync("checkout-inactive-1");
        Assert.NotNull(attempt);
        Assert.Equal(CheckoutAttemptStatus.Completed, attempt.Status);
        Assert.Equal(409, attempt.ResponseStatusCode);
    }

    private async Task<string> GetAliceCartETagAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/cart");
        request.Headers.Add("X-Test-User", Alice);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return response.Headers.ETag!.Tag;
    }

    private async Task<string> AddItemToAliceCartAsync(Guid productId, int quantity)
    {
        var etag = await GetAliceCartETagAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/cart/items")
        {
            Content = JsonContent.Create(new { productId, quantity })
        };
        request.Headers.Add("X-Test-User", Alice);
        request.Headers.Add("If-Match", etag);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return response.Headers.ETag!.Tag;
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

    private async Task ExecuteSqlRawAsync(string sql, params object[] parameters)
    {
        await using var context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(sql, parameters);
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

    private async Task<int> GetOrdersCountAsync()
    {
        await using var context = CreateContext();
        return await context.Orders.AsNoTracking().CountAsync();
    }

    private async Task<int> GetAliceCartItemQuantityAsync(Guid productId)
    {
        await using var context = CreateContext();
        var cartId = await GetAliceCartIdAsync(context);
        if (cartId is null)
        {
            return 0;
        }

        return await context.CartItems
            .AsNoTracking()
            .Where(item => item.CartId == cartId && item.ProductId == productId)
            .Select(item => (int?)item.Quantity)
            .FirstOrDefaultAsync() ?? 0;
    }

    private async Task<CheckoutAttempt?> GetCheckoutAttemptAsync(string idempotencyKey)
    {
        await using var context = CreateContext();
        return await context.CheckoutAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(attempt => attempt.IdempotencyKey == idempotencyKey);
    }

    private static async Task<Guid?> GetAliceCartIdAsync(AppDbContext context)
    {
        var aliceUserId = await context.Users
            .Where(user => user.Email == SeedData.AliceEmail)
            .Select(user => user.Id)
            .FirstAsync();

        return await context.Carts
            .Where(cart => cart.UserId == aliceUserId)
            .Select(cart => (Guid?)cart.Id)
            .FirstOrDefaultAsync();
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options);

    private sealed record ErrorResponse(string Code);
}

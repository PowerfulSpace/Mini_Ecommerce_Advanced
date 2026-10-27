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
            "UPDATE Products SET Available = {0} WHERE Id = {1}",
            3,
            productId);

        var response = await _client.SendAsync(CreateCheckoutRequest("checkout-insufficient-1", etag));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("insufficient_stock", error.Code);
        Assert.Equal($"Insufficient stock for: {WirelessMouse} ({productId})", error.Detail);

        Assert.Equal(0, await GetOrdersCountAsync());
        Assert.Equal(3, await GetAvailableAsync(WirelessMouse));
        Assert.Equal(0, await GetReservedAsync(WirelessMouse));
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
        Assert.Equal($"Unavailable products: {WirelessMouse} ({productId})", error.Detail);

        Assert.Equal(0, await GetOrdersCountAsync());
        Assert.Equal(100, await GetAvailableAsync(WirelessMouse));
        Assert.Equal(0, await GetReservedAsync(WirelessMouse));
        Assert.Equal(2, await GetAliceCartItemQuantityAsync(productId));

        var attempt = await GetCheckoutAttemptAsync("checkout-inactive-1");
        Assert.NotNull(attempt);
        Assert.Equal(CheckoutAttemptStatus.Completed, attempt.Status);
        Assert.Equal(409, attempt.ResponseStatusCode);
    }

    [Fact]
    public async Task PostOrders_WithMultipleInsufficientProducts_ReturnsAllInDetail()
    {
        var products = await GetActiveProductsAsync(count: 2);
        var first = products[0];
        var second = products[1];

        var etag = await AddItemToAliceCartAsync(first.Id, 1);
        etag = await AddItemToAliceCartAsync(second.Id, 1);

        await SetAvailableAsync(first.Id, 0);
        await SetAvailableAsync(second.Id, 0);

        var response = await _client.SendAsync(
            CreateCheckoutRequest("checkout-insufficient-multi-1", etag));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("insufficient_stock", error.Code);
        Assert.Equal(
            $"Insufficient stock for: {first.Name} ({first.Id}), {second.Name} ({second.Id})",
            error.Detail);

        Assert.Equal(0, await GetOrdersCountAsync());
        Assert.Equal(0, await GetAvailableByIdAsync(first.Id));
        Assert.Equal(0, await GetAvailableByIdAsync(second.Id));
        Assert.Equal(0, await GetReservedByIdAsync(first.Id));
        Assert.Equal(0, await GetReservedByIdAsync(second.Id));
        Assert.Equal(2, await GetAliceCartItemCountAsync());

        var attempt = await GetCheckoutAttemptAsync("checkout-insufficient-multi-1");
        Assert.NotNull(attempt);
        Assert.Equal(CheckoutAttemptStatus.Completed, attempt.Status);
        Assert.Equal(409, attempt.ResponseStatusCode);
    }

    [Fact]
    public async Task PostOrders_WithInactiveAndInsufficientProducts_ReturnsOnlyUnavailableProducts()
    {
        var products = await GetActiveProductsAsync(count: 2);
        var unavailable = products[0];
        var insufficient = products[1];

        var etag = await AddItemToAliceCartAsync(unavailable.Id, 1);
        etag = await AddItemToAliceCartAsync(insufficient.Id, 1);

        await SetProductActiveAsync(unavailable.Id, isActive: false);
        await SetAvailableAsync(insufficient.Id, 0);

        var response = await _client.SendAsync(
            CreateCheckoutRequest("checkout-priority-1", etag));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("product_unavailable", error.Code);
        Assert.Equal(
            $"Unavailable products: {unavailable.Name} ({unavailable.Id})",
            error.Detail);

        Assert.Equal(0, await GetOrdersCountAsync());
        Assert.Equal(0, await GetAvailableByIdAsync(insufficient.Id));
        Assert.Equal(0, await GetReservedByIdAsync(insufficient.Id));
        Assert.Equal(2, await GetAliceCartItemCountAsync());

        var attempt = await GetCheckoutAttemptAsync("checkout-priority-1");
        Assert.NotNull(attempt);
        Assert.Equal(CheckoutAttemptStatus.Completed, attempt.Status);
        Assert.Equal(409, attempt.ResponseStatusCode);
    }

    [Fact]
    public async Task PostOrders_WithPartiallySelectedCartAndSelectedItemInsufficientStock_RollsBackEverything()
    {
        var products = await GetActiveProductsAsync(count: 3);
        var insufficient = products[0];
        var sufficient = products[1];
        var unselected = products[2];

        var etag = await AddItemToAliceCartAsync(insufficient.Id, 1);
        etag = await AddItemToAliceCartAsync(sufficient.Id, 1);
        etag = await AddItemToAliceCartAsync(unselected.Id, 1);
        etag = await PutSelectionAsync(
            etag,
            (insufficient.Id, true),
            (sufficient.Id, true),
            (unselected.Id, false));

        await SetAvailableAsync(insufficient.Id, 0);

        var response = await _client.SendAsync(
            CreateCheckoutRequest("checkout-partial-insufficient-1", etag));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("insufficient_stock", error.Code);
        Assert.Equal(
            $"Insufficient stock for: {insufficient.Name} ({insufficient.Id})",
            error.Detail);

        Assert.Equal(0, await GetOrdersCountAsync());
        Assert.Equal(0, await GetAvailableByIdAsync(insufficient.Id));
        Assert.Equal(0, await GetReservedByIdAsync(insufficient.Id));
        Assert.Equal(sufficient.Available, await GetAvailableByIdAsync(sufficient.Id));
        Assert.Equal(0, await GetReservedByIdAsync(sufficient.Id));
        Assert.Equal(unselected.Available, await GetAvailableByIdAsync(unselected.Id));
        Assert.Equal(0, await GetReservedByIdAsync(unselected.Id));

        var cartItems = await GetAliceCartItemsAsync();
        Assert.Equal(3, cartItems.Count);
        Assert.All(cartItems, item => Assert.Equal(1, item.Quantity));
        Assert.Single(cartItems, item => item.ProductId == insufficient.Id && item.IsSelected);
        Assert.Single(cartItems, item => item.ProductId == sufficient.Id && item.IsSelected);
        Assert.Single(cartItems, item => item.ProductId == unselected.Id && !item.IsSelected);

        var attempt = await GetCheckoutAttemptAsync("checkout-partial-insufficient-1");
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

    private async Task<string> PutSelectionAsync(
        string ifMatch,
        params (Guid ProductId, bool IsSelected)[] selections)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, "/api/cart/selection")
        {
            Content = JsonContent.Create(new
            {
                items = selections.Select(selection => new
                {
                    productId = selection.ProductId,
                    isSelected = selection.IsSelected
                })
            })
        };
        request.Headers.Add("X-Test-User", Alice);
        request.Headers.Add("If-Match", ifMatch);
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

    private async Task<List<ProductSnapshot>> GetActiveProductsAsync(int count)
    {
        await using var context = CreateContext();
        var products = await context.Products
            .AsNoTracking()
            .Where(product => product.IsActive && product.Available >= 1)
            .Select(product => new ProductSnapshot(
                product.Id,
                product.Name,
                product.Available))
            .ToListAsync();

        return products
            .OrderBy(product => product.Id)
            .Take(count)
            .ToList();
    }

    private async Task SetAvailableAsync(Guid productId, int available)
    {
        await using var context = CreateContext();
        await context.Products
            .Where(product => product.Id == productId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(product => product.Available, available));
    }

    private async Task SetProductActiveAsync(Guid productId, bool isActive)
    {
        await using var context = CreateContext();
        await context.Products
            .Where(product => product.Id == productId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(product => product.IsActive, isActive));
    }

    private async Task<int> GetAvailableAsync(string name)
    {
        await using var context = CreateContext();
        return await context.Products
            .AsNoTracking()
            .Where(product => product.Name == name)
            .Select(product => product.Available)
            .FirstAsync();
    }

    private async Task<int> GetAvailableByIdAsync(Guid productId)
    {
        await using var context = CreateContext();
        return await context.Products
            .AsNoTracking()
            .Where(product => product.Id == productId)
            .Select(product => product.Available)
            .FirstAsync();
    }

    private async Task<int> GetReservedAsync(string name)
    {
        await using var context = CreateContext();
        return await context.Products
            .AsNoTracking()
            .Where(product => product.Name == name)
            .Select(product => product.Reserved)
            .FirstAsync();
    }

    private async Task<int> GetReservedByIdAsync(Guid productId)
    {
        await using var context = CreateContext();
        return await context.Products
            .AsNoTracking()
            .Where(product => product.Id == productId)
            .Select(product => product.Reserved)
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

    private async Task<int> GetAliceCartItemCountAsync()
    {
        await using var context = CreateContext();
        var cartId = await GetAliceCartIdAsync(context);
        if (cartId is null)
        {
            return 0;
        }

        return await context.CartItems
            .AsNoTracking()
            .CountAsync(item => item.CartId == cartId);
    }

    private async Task<List<CartItemSnapshot>> GetAliceCartItemsAsync()
    {
        await using var context = CreateContext();
        var cartId = await GetAliceCartIdAsync(context);
        if (cartId is null)
        {
            return new List<CartItemSnapshot>();
        }

        return await context.CartItems
            .AsNoTracking()
            .Where(item => item.CartId == cartId)
            .OrderBy(item => item.ProductId)
            .Select(item => new CartItemSnapshot(item.ProductId, item.Quantity, item.IsSelected))
            .ToListAsync();
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

    private sealed record ProductSnapshot(Guid Id, string Name, int Available);

    private sealed record CartItemSnapshot(Guid ProductId, int Quantity, bool IsSelected);

    private sealed record ErrorResponse(string Code, string Detail);
}

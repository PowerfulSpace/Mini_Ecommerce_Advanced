using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.Infrastructure.Persistence;
using PS.MiniEcommerce.Infrastructure.Seed;
using PS.MiniEcommerce.IntegrationTests.Fixtures;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class CheckoutEndpointTests : IAsyncLifetime
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const string WirelessMouse = "Wireless Mouse";

    private readonly TestDatabaseFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CheckoutEndpointTests(TestDatabaseFixture fixture)
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
    public async Task PostOrders_WhenCartValid_Returns201WithOrderBody()
    {
        var productId = await GetActiveProductIdAsync(WirelessMouse);
        var etag = await AddItemToAliceCartAsync(productId, 1);

        var response = await _client.SendAsync(CreateCheckoutRequest("checkout-valid-1", etag));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.NotNull(response.Headers.ETag);

        var order = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(order);
        Assert.Equal("Created", order.Status);
        Assert.Equal(19.99m, order.TotalAmount);
        var item = Assert.Single(order.Items);
        Assert.Equal(productId, item.ProductId);
        Assert.Equal(WirelessMouse, item.ProductName);
        Assert.Equal(19.99m, item.UnitPrice);

        var stock = await GetStockAsync(WirelessMouse);
        Assert.Equal(99, stock.Available);
        Assert.Equal(1, stock.Reserved);
        Assert.Equal(0, await GetAliceCartItemCountAsync());

        var attempt = await GetCheckoutAttemptAsync("checkout-valid-1");
        Assert.NotNull(attempt);
        Assert.Equal(CheckoutAttemptStatus.Completed, attempt.Status);
        Assert.Equal(201, attempt.ResponseStatusCode);
        Assert.NotNull(attempt.OrderId);
    }

    [Fact]
    public async Task PostOrders_WhenProductChangesAfterCartReadButBeforeStockDeduction_SnapshotsLockedValues()
    {
        var products = await GetActiveProductsAsync(count: 3);
        var first = products[0];
        var blocked = products[1];
        var target = products[2];
        var updatedName = target.Name + " (updated)";
        var updatedPrice = Money.FromDecimal(target.Price.Amount + 10m);

        var etag = await AddItemToAliceCartAsync(first.Id, 1);
        etag = await AddItemToAliceCartAsync(blocked.Id, 1);
        etag = await AddItemToAliceCartAsync(target.Id, 1);

        await using var blockerContext = CreateContext();
        await using var blockerTransaction = await blockerContext.Database.BeginTransactionAsync();

        await blockerContext.Products
            .FromSqlInterpolated($"SELECT * FROM Products WITH (UPDLOCK, ROWLOCK) WHERE Id = {blocked.Id}")
            .AsNoTracking()
            .ToListAsync();

        var checkout = _client.SendAsync(
            CreateCheckoutRequest("checkout-snapshot-race-1", etag));

        await WaitForAvailableWithNoLockAsync(first.Id, first.Available - 1);

        await blockerContext.Products
            .Where(product => product.Id == target.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(product => product.Name, updatedName)
                .SetProperty(product => product.Price, updatedPrice));

        await blockerTransaction.CommitAsync();

        var response = await checkout.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(order);

        var item = Assert.Single(order.Items, orderItem => orderItem.ProductId == target.Id);
        Assert.Equal(updatedName, item.ProductName);
        Assert.Equal(updatedPrice.Amount, item.UnitPrice);
        Assert.Equal(updatedPrice.Amount * item.Quantity, item.LineTotal);
    }

    [Fact]
    public async Task PostOrders_WhenStockChangesAfterPreliminaryCheck_ReturnsInsufficientStockFromUpdatePath()
    {
        var products = await GetActiveProductsAsync(count: 3);
        var first = products[0];
        var blocked = products[1];
        var target = products[2];

        var etag = await AddItemToAliceCartAsync(first.Id, 1);
        etag = await AddItemToAliceCartAsync(blocked.Id, 1);
        etag = await AddItemToAliceCartAsync(target.Id, 1);

        await using var blockerContext = CreateContext();
        await using var blockerTransaction = await blockerContext.Database.BeginTransactionAsync();

        await blockerContext.Products
            .FromSqlInterpolated($"SELECT * FROM Products WITH (UPDLOCK, ROWLOCK) WHERE Id = {blocked.Id}")
            .AsNoTracking()
            .ToListAsync();

        var checkout = _client.SendAsync(
            CreateCheckoutRequest("checkout-update-path-stock-race-1", etag));

        await WaitForAvailableWithNoLockAsync(first.Id, first.Available - 1);

        await blockerContext.Products
            .Where(product => product.Id == target.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(product => product.Available, 0));

        await blockerTransaction.CommitAsync();

        var response = await checkout.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("insufficient_stock", error.Code);
        Assert.Equal($"Insufficient stock for: {target.Name} ({target.Id})", error.Detail);

        Assert.Equal(0, await GetOrdersCountAsync());
        Assert.Equal(3, await GetAliceCartItemCountAsync());

        var firstStock = await GetStockAsync(first.Name);
        Assert.Equal(first.Available, firstStock.Available);
        Assert.Equal(0, firstStock.Reserved);

        var blockedStock = await GetStockAsync(blocked.Name);
        Assert.Equal(blocked.Available, blockedStock.Available);
        Assert.Equal(0, blockedStock.Reserved);

        var targetStock = await GetStockAsync(target.Name);
        Assert.Equal(0, targetStock.Available);
        Assert.Equal(0, targetStock.Reserved);

        var attempt = await GetCheckoutAttemptAsync("checkout-update-path-stock-race-1");
        Assert.NotNull(attempt);
        Assert.Equal(CheckoutAttemptStatus.Completed, attempt.Status);
        Assert.Equal(409, attempt.ResponseStatusCode);
    }

    [Fact]
    public async Task PostOrders_TwoConcurrentCheckoutsForLastItem_OneSucceedsAndOneGetsInsufficientStock()
    {
        var productId = await GetActiveProductIdAsync("Limited Edition Mug");
        var aliceEtag = await AddItemToCartAsync(Alice, productId, 1);
        var bobEtag = await AddItemToCartAsync(Bob, productId, 1);

        var requests = new[]
        {
            Task.Run(() => _client.SendAsync(
                CreateCheckoutRequest(Alice, "checkout-race-last-item-alice", aliceEtag))),
            Task.Run(() => _client.SendAsync(
                CreateCheckoutRequest(Bob, "checkout-race-last-item-bob", bobEtag)))
        };

        var responses = await Task.WhenAll(requests);

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        var conflict = Assert.Single(
            responses,
            response => response.StatusCode == HttpStatusCode.Conflict);
        var error = await conflict.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("insufficient_stock", error.Code);

        var stock = await GetStockAsync("Limited Edition Mug");
        Assert.Equal(0, stock.Available);
        Assert.Equal(1, stock.Reserved);
        Assert.Equal(1, await GetOrdersCountAsync());
    }

    [Fact]
    public async Task PostOrders_ReplaySameKeyAndFingerprint_ReturnsSameBodyWithoutSecondOrder()
    {
        var productId = await GetActiveProductIdAsync(WirelessMouse);
        var etag = await AddItemToAliceCartAsync(productId, 1);

        var first = await _client.SendAsync(CreateCheckoutRequest("checkout-replay-1", etag));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var body1 = await first.Content.ReadAsStringAsync();

        var second = await _client.SendAsync(CreateCheckoutRequest("checkout-replay-1", etag));

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var body2 = await second.Content.ReadAsStringAsync();
        Assert.Equal(body1, body2);
        Assert.Null(second.Headers.Location);
        Assert.Null(second.Headers.ETag);

        Assert.Equal(1, await GetOrdersCountAsync());
    }

    [Fact]
    public async Task PostOrders_ReuseKeyWithDifferentFingerprint_Returns409IdempotencyKeyReused()
    {
        var productId = await GetActiveProductIdAsync(WirelessMouse);
        var etag1 = await AddItemToAliceCartAsync(productId, 1);

        var first = await _client.SendAsync(CreateCheckoutRequest("checkout-reuse-1", etag1));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var etag2 = await AddItemToAliceCartAsync(productId, 1);
        Assert.NotEqual(etag1, etag2);

        var second = await _client.SendAsync(CreateCheckoutRequest("checkout-reuse-1", etag2));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var error = await second.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("idempotency_key_reused", error.Code);
    }

    [Fact]
    public async Task PostOrders_WithStaleETag_Returns412CartVersionConflict()
    {
        var productId = await GetActiveProductIdAsync(WirelessMouse);
        var etag1 = await AddItemToAliceCartAsync(productId, 1);
        var etag2 = await AddItemToAliceCartAsync(productId, 1);
        Assert.NotEqual(etag1, etag2);

        var response = await _client.SendAsync(CreateCheckoutRequest("checkout-stale-1", etag1));

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("cart_version_conflict", error.Code);

        var attempt = await GetCheckoutAttemptAsync("checkout-stale-1");
        Assert.NotNull(attempt);
        Assert.Equal(CheckoutAttemptStatus.Completed, attempt.Status);
        Assert.Equal(412, attempt.ResponseStatusCode);
    }

    [Fact]
    public async Task PostOrders_WithStaleETagReplay_ReturnsSame412()
    {
        var productId = await GetActiveProductIdAsync(WirelessMouse);
        var etag1 = await AddItemToAliceCartAsync(productId, 1);
        await AddItemToAliceCartAsync(productId, 1);

        var first = await _client.SendAsync(CreateCheckoutRequest("checkout-stale-replay-1", etag1));
        Assert.Equal(HttpStatusCode.PreconditionFailed, first.StatusCode);
        var body1 = await first.Content.ReadAsStringAsync();

        var attemptBefore = await GetCheckoutAttemptAsync("checkout-stale-replay-1");
        Assert.NotNull(attemptBefore);
        var updatedAtBefore = attemptBefore.UpdatedAtUtc;

        var second = await _client.SendAsync(CreateCheckoutRequest("checkout-stale-replay-1", etag1));

        Assert.Equal(HttpStatusCode.PreconditionFailed, second.StatusCode);
        var body2 = await second.Content.ReadAsStringAsync();
        Assert.Equal(body1, body2);

        var attemptAfter = await GetCheckoutAttemptAsync("checkout-stale-replay-1");
        Assert.NotNull(attemptAfter);
        Assert.Equal(updatedAtBefore, attemptAfter.UpdatedAtUtc);
    }

    [Fact]
    public async Task PostOrders_WithEmptyCart_Returns409CartEmpty()
    {
        var etag = await GetAliceCartETagAsync();

        var response = await _client.SendAsync(CreateCheckoutRequest("checkout-empty-1", etag));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("cart_empty", error.Code);
    }

    [Fact]
    public async Task PostOrders_WhenReplayingSavedError_ReturnsProblemDetailsWithInstance()
    {
        var etag = await GetAliceCartETagAsync();
        const string idempotencyKey = "checkout-d4-instance-1";

        var first = await _client.SendAsync(CreateCheckoutRequest(idempotencyKey, etag));

        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
        Assert.Equal("application/problem+json", first.Content.Headers.ContentType?.MediaType);
        var firstBody = await first.Content.ReadAsStringAsync();

        var attempt = await GetCheckoutAttemptAsync(idempotencyKey);
        Assert.NotNull(attempt);
        Assert.Equal(CheckoutAttemptStatus.Completed, attempt.Status);
        var savedPayload = attempt.ResponsePayload;
        Assert.NotNull(savedPayload);
        Assert.DoesNotContain("\"instance\"", savedPayload);

        using var firstProblem = JsonDocument.Parse(firstBody);
        using var savedProblem = JsonDocument.Parse(savedPayload!);
        Assert.Equal("/api/orders", firstProblem.RootElement.GetProperty("instance").GetString());
        Assert.Equal(
            savedProblem.RootElement.GetProperty("type").GetString(),
            firstProblem.RootElement.GetProperty("type").GetString());
        Assert.Equal(
            savedProblem.RootElement.GetProperty("title").GetString(),
            firstProblem.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            savedProblem.RootElement.GetProperty("status").GetInt32(),
            firstProblem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            savedProblem.RootElement.GetProperty("detail").GetString(),
            firstProblem.RootElement.GetProperty("detail").GetString());
        Assert.Equal(
            savedProblem.RootElement.GetProperty("code").GetString(),
            firstProblem.RootElement.GetProperty("code").GetString());

        var second = await _client.SendAsync(CreateCheckoutRequest(idempotencyKey, etag));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        var secondBody = await second.Content.ReadAsStringAsync();
        Assert.Equal(firstBody, secondBody);

        using var secondProblem = JsonDocument.Parse(secondBody);
        Assert.Equal("/api/orders", secondProblem.RootElement.GetProperty("instance").GetString());
    }

    [Fact]
    public async Task PostOrders_WithoutIfMatch_Returns428PreconditionRequired()
    {
        var productId = await GetActiveProductIdAsync(WirelessMouse);
        await AddItemToAliceCartAsync(productId, 1);

        var response = await _client.SendAsync(CreateCheckoutRequest("checkout-noifmatch-1", ifMatch: null));

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("precondition_required", error.Code);
    }

    [Fact]
    public async Task PostOrders_WithoutIfMatch_DoesNotConsumeIdempotencyKey()
    {
        var productId = await GetActiveProductIdAsync(WirelessMouse);
        var etag = await AddItemToAliceCartAsync(productId, 1);
        const string idempotencyKey = "checkout-noifmatch-retry-1";

        var withoutIfMatch = await _client.SendAsync(
            CreateCheckoutRequest(idempotencyKey, ifMatch: null));

        Assert.Equal(HttpStatusCode.PreconditionRequired, withoutIfMatch.StatusCode);
        var error = await withoutIfMatch.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("precondition_required", error.Code);
        Assert.Null(await GetCheckoutAttemptAsync(idempotencyKey));

        var retry = await _client.SendAsync(CreateCheckoutRequest(idempotencyKey, etag));

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        var order = await retry.Content.ReadFromJsonAsync<OrderResponse>();
        Assert.NotNull(order);
        Assert.Equal("Created", order.Status);
        Assert.Equal(1, await GetOrdersCountAsync());
        Assert.Equal(0, await GetAliceCartItemCountAsync());

        var attempt = await GetCheckoutAttemptAsync(idempotencyKey);
        Assert.NotNull(attempt);
        Assert.Equal(CheckoutAttemptStatus.Completed, attempt.Status);
        Assert.Equal(201, attempt.ResponseStatusCode);
    }

    [Fact]
    public async Task PostOrders_WithWildcardIfMatch_Returns400ValidationError()
    {
        var productId = await GetActiveProductIdAsync(WirelessMouse);
        await AddItemToAliceCartAsync(productId, 1);

        var response = await _client.SendAsync(CreateCheckoutRequest("checkout-wildcard-1", "*"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("validation_error", error.Code);
        Assert.Null(await GetCheckoutAttemptAsync("checkout-wildcard-1"));
    }

    [Fact]
    public async Task PostOrders_WithoutIdempotencyKey_Returns400ValidationError()
    {
        var productId = await GetActiveProductIdAsync(WirelessMouse);
        var etag = await AddItemToAliceCartAsync(productId, 1);

        var response = await _client.SendAsync(CreateCheckoutRequest(idempotencyKey: null, etag));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("validation_error", error.Code);
    }

    [Fact]
    public async Task PostOrders_Unauthenticated_Returns401Unauthenticated()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.Add("Idempotency-Key", "checkout-unauth-1");
        request.Headers.Add("If-Match", "\"AAAAAAAAB9Q=\"");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("unauthenticated", error.Code);
    }

    private Task<string> GetAliceCartETagAsync() => GetCartETagAsync(Alice);

    private Task<string> AddItemToAliceCartAsync(Guid productId, int quantity) =>
        AddItemToCartAsync(Alice, productId, quantity);

    private async Task<string> GetCartETagAsync(string testUser)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/cart");
        request.Headers.Add("X-Test-User", testUser);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return response.Headers.ETag!.Tag;
    }

    private async Task<string> AddItemToCartAsync(string testUser, Guid productId, int quantity)
    {
        var etag = await GetCartETagAsync(testUser);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/cart/items")
        {
            Content = JsonContent.Create(new { productId, quantity })
        };
        request.Headers.Add("X-Test-User", testUser);
        request.Headers.Add("If-Match", etag);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return response.Headers.ETag!.Tag;
    }

    private static HttpRequestMessage CreateCheckoutRequest(string? idempotencyKey, string? ifMatch) =>
        CreateCheckoutRequest(Alice, idempotencyKey, ifMatch);

    private static HttpRequestMessage CreateCheckoutRequest(
        string testUser,
        string? idempotencyKey,
        string? ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        request.Headers.Add("X-Test-User", testUser);
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
                product.Price,
                product.Available,
                product.Reserved))
            .ToListAsync();

        return products
            .OrderBy(product => product.Id)
            .Take(count)
            .ToList();
    }

    private async Task WaitForAvailableWithNoLockAsync(Guid productId, int expectedAvailable)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (DateTime.UtcNow < deadline)
        {
            await using var context = CreateContext();
            var available = await context.Products
                .FromSqlInterpolated($"SELECT * FROM Products WITH (NOLOCK) WHERE Id = {productId}")
                .AsNoTracking()
                .Select(product => product.Available)
                .SingleAsync();

            if (available == expectedAvailable)
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException(
            $"Product {productId} did not reach available quantity {expectedAvailable} before the timeout.");
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

    private async Task<int> GetOrdersCountAsync()
    {
        await using var context = CreateContext();
        return await context.Orders.AsNoTracking().CountAsync();
    }

    private async Task<int> GetAliceCartItemCountAsync()
    {
        await using var context = CreateContext();
        var cartId = await GetAliceCartIdAsync(context);
        if (cartId is null)
        {
            return 0;
        }

        return await context.CartItems.AsNoTracking().CountAsync(item => item.CartId == cartId);
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

    private sealed record ProductSnapshot(Guid Id, string Name, Money Price, int Available, int Reserved);

    private sealed record StockSnapshot(int Available, int Reserved);

    private sealed record OrderResponse(Guid Id, string Status, DateTime CreatedAtUtc, decimal TotalAmount, List<OrderItemResponse> Items);

    private sealed record OrderItemResponse(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

    private sealed record ErrorResponse(string Code, string Detail);
}

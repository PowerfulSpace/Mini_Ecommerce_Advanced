using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.Infrastructure.Persistence;
using PS.MiniEcommerce.Infrastructure.Seed;
using PS.MiniEcommerce.IntegrationTests.Fixtures;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class CheckoutRecoveryTests : IAsyncLifetime
{
    private const string Alice = "alice";
    private const string WirelessMouse = "Wireless Mouse";

    private readonly TestDatabaseFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CheckoutRecoveryTests(TestDatabaseFixture fixture)
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
    public async Task PostOrders_WhenStaleInProgressAttemptExists_RecoversAndCompletesCheckout()
    {
        var productId = await GetActiveProductIdAsync(WirelessMouse);
        var stockBefore = await GetStockQuantityAsync(WirelessMouse);

        var etag = await AddItemToAliceCartAsync(productId, quantity: 1);
        var fingerprint = ComputeFingerprint(etag);

        await CreateStaleInProgressAttemptAsync("stale-recovery-1", fingerprint);

        var response = await _client.SendAsync(CreateCheckoutRequest("stale-recovery-1", etag));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using (var document = JsonDocument.Parse(body))
        {
            Assert.Equal("Created", document.RootElement.GetProperty("status").GetString());
        }

        var attempt = await GetCheckoutAttemptAsync("stale-recovery-1");
        Assert.NotNull(attempt);
        Assert.Equal(CheckoutAttemptStatus.Completed, attempt.Status);
        Assert.NotNull(attempt.OrderId);
        Assert.Equal(201, attempt.ResponseStatusCode);

        Assert.Equal(1, await GetOrdersCountAsync());
        Assert.Equal(stockBefore - 1, await GetStockQuantityAsync(WirelessMouse));
        Assert.Equal(0, await GetAliceCartItemCountAsync());
    }

    [Fact]
    public async Task PostOrders_WhenActiveLeaseAttemptExists_Returns503IdempotencyInProgress()
    {
        var productId = await GetActiveProductIdAsync(WirelessMouse);
        var etag = await AddItemToAliceCartAsync(productId, quantity: 1);
        var fingerprint = ComputeFingerprint(etag);

        await CreateActiveInProgressAttemptAsync("active-lease-1", fingerprint);

        var response = await _client.SendAsync(CreateCheckoutRequest("active-lease-1", etag));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("idempotency_in_progress", error.Code);

        var attempt = await GetCheckoutAttemptAsync("active-lease-1");
        Assert.NotNull(attempt);
        Assert.Equal(CheckoutAttemptStatus.InProgress, attempt.Status);
    }

    [Fact]
    public async Task PostOrders_WhenConcurrentAttemptCompletesWithinBoundedWait_ReturnsReplay()
    {
        var productId = await GetActiveProductIdAsync(WirelessMouse);
        var etag = await AddItemToAliceCartAsync(productId, quantity: 1);
        var fingerprint = ComputeFingerprint(etag);

        const string replayPayload =
            "{\"id\":\"6f1e2a44-0000-0000-0000-000000000000\",\"status\":\"Created\"}";

        await CreateActiveInProgressAttemptAsync("bounded-wait-1", fingerprint);

        var completing = Task.Run(async () =>
        {
            await Task.Delay(500);

            await using var context = CreateContext();
            var attempt = await context.CheckoutAttempts
                .FirstAsync(candidate => candidate.IdempotencyKey == "bounded-wait-1");

            attempt.MarkCompletedWithError(201, replayPayload, DateTime.UtcNow);
            await context.SaveChangesAsync();
        });

        var response = await _client.SendAsync(CreateCheckoutRequest("bounded-wait-1", etag));
        await completing;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(replayPayload, body);
    }

    [Fact]
    public async Task PostOrders_TwoConcurrentCheckouts_SameKey_OneSucceedsOneWaitsOrConflicts()
    {
        var productId = await GetActiveProductIdAsync(WirelessMouse);
        var etag = await AddItemToAliceCartAsync(productId, quantity: 1);

        var requests = new[]
        {
            Task.Run(() => _client.SendAsync(CreateCheckoutRequest("concurrent-checkout-1", etag))),
            Task.Run(() => _client.SendAsync(CreateCheckoutRequest("concurrent-checkout-1", etag)))
        };

        var responses = await Task.WhenAll(requests);
        var statuses = responses.Select(response => response.StatusCode).ToList();

        Assert.Contains(HttpStatusCode.Created, statuses);
        Assert.All(statuses, status => Assert.True(
            status == HttpStatusCode.Created || status == HttpStatusCode.ServiceUnavailable,
            $"Unexpected status code: {status}"));

        Assert.Equal(1, await GetOrdersCountAsync());
    }

    private async Task<Guid> GetActiveProductIdAsync(string name)
    {
        await using var context = CreateContext();
        return await context.Products
            .Where(product => product.Name == name && product.IsActive)
            .Select(product => product.Id)
            .FirstAsync();
    }

    private async Task<string> AddItemToAliceCartAsync(Guid productId, int quantity)
    {
        var cartRequest = new HttpRequestMessage(HttpMethod.Get, "/api/cart");
        cartRequest.Headers.Add("X-Test-User", Alice);
        var cartResponse = await _client.SendAsync(cartRequest);
        cartResponse.EnsureSuccessStatusCode();
        var etag = cartResponse.Headers.ETag!.Tag;

        var addRequest = new HttpRequestMessage(HttpMethod.Post, "/api/cart/items")
        {
            Content = JsonContent.Create(new { productId, quantity })
        };
        addRequest.Headers.Add("X-Test-User", Alice);
        addRequest.Headers.Add("If-Match", etag);
        var addResponse = await _client.SendAsync(addRequest);
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

    private Task<CheckoutAttempt> CreateStaleInProgressAttemptAsync(string idempotencyKey, byte[] fingerprint)
    {
        return CreateInProgressAttemptAsync(
            idempotencyKey,
            fingerprint,
            expired: true);
    }

    private Task<CheckoutAttempt> CreateActiveInProgressAttemptAsync(string idempotencyKey, byte[] fingerprint)
    {
        return CreateInProgressAttemptAsync(
            idempotencyKey,
            fingerprint,
            expired: false);
    }

    private async Task<CheckoutAttempt> CreateInProgressAttemptAsync(
        string idempotencyKey,
        byte[] fingerprint,
        bool expired)
    {
        await using var context = CreateContext();

        var aliceId = await context.Users
            .Where(user => user.Email == SeedData.AliceEmail)
            .Select(user => user.Id)
            .FirstAsync();

        var attempt = CheckoutAttempt.Create(
            aliceId,
            idempotencyKey,
            fingerprint,
            DateTime.UtcNow,
            TimeSpan.FromSeconds(30));

        context.CheckoutAttempts.Add(attempt);
        await context.SaveChangesAsync();

        if (expired)
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE CheckoutAttempts SET LeaseExpiresAtUtc = {DateTime.UtcNow.AddSeconds(-10)} WHERE Id = {attempt.Id}");
        }

        return attempt;
    }

    private async Task<CheckoutAttempt?> GetCheckoutAttemptAsync(string idempotencyKey)
    {
        await using var context = CreateContext();
        return await context.CheckoutAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(attempt => attempt.IdempotencyKey == idempotencyKey);
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

    private async Task<int> GetAliceCartItemCountAsync()
    {
        await using var context = CreateContext();

        var aliceId = await context.Users
            .Where(user => user.Email == SeedData.AliceEmail)
            .Select(user => user.Id)
            .FirstAsync();

        var cartId = await context.Carts
            .Where(cart => cart.UserId == aliceId)
            .Select(cart => (Guid?)cart.Id)
            .FirstOrDefaultAsync();

        if (cartId is null)
        {
            return 0;
        }

        return await context.CartItems.AsNoTracking().CountAsync(item => item.CartId == cartId);
    }

    private static byte[] ComputeFingerprint(string ifMatch)
    {
        var input = $"POST /api/orders\n{ifMatch}";
        return SHA256.HashData(Encoding.UTF8.GetBytes(input));
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options);

    private sealed record ErrorResponse(string Code);
}

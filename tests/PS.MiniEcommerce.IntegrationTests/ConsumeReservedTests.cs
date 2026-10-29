using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Domain.Exceptions;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.Infrastructure.Payments;
using PS.MiniEcommerce.Infrastructure.Persistence;
using PS.MiniEcommerce.Infrastructure.Seed;
using PS.MiniEcommerce.IntegrationTests.Fixtures;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class ConsumeReservedTests : IAsyncLifetime
{
    private const string Alice = "alice";
    private const string WirelessMouse = "Wireless Mouse";

    private readonly TestDatabaseFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ConsumeReservedTests(TestDatabaseFixture fixture)
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
    public async Task ConsumeReserved_WhenReservedStockAvailable_DecreasesReservedAndKeepsAvailable()
    {
        var orderId = await CreateOrderAsync(WirelessMouse, quantity: 2);
        var before = await GetStockAsync(WirelessMouse);

        Assert.Equal(2, before.Reserved);

        await using (var context = CreateContext())
        {
            var repository = new OrderRepository(
                context,
                new PaymentRepository(context),
                new PaymentRefundService(),
                new OutboxRepository(context));
            await repository.ConsumeReservedAsync(orderId, CancellationToken.None);
        }

        var after = await GetStockAsync(WirelessMouse);
        Assert.Equal(before.Available, after.Available);
        Assert.Equal(before.Reserved - 2, after.Reserved);
    }

    [Fact]
    public async Task ConsumeReserved_WhenReservedStockInsufficient_ThrowsConflictAndDoesNotChangeStock()
    {
        var productId = await GetActiveProductIdAsync(WirelessMouse);
        var orderId = await CreateOrderAsync(WirelessMouse, quantity: 2);
        var before = await GetStockAsync(WirelessMouse);

        await SetReservedAsync(productId, 0);

        await using (var context = CreateContext())
        {
            var repository = new OrderRepository(
                context,
                new PaymentRepository(context),
                new PaymentRefundService(),
                new OutboxRepository(context));

            var exception = await Assert.ThrowsAsync<DomainConflictException>(
                () => repository.ConsumeReservedAsync(orderId, CancellationToken.None));

            Assert.Equal("conflict", exception.Code);
        }

        var after = await GetStockAsync(WirelessMouse);
        Assert.Equal(before.Available, after.Available);
        Assert.Equal(0, after.Reserved);
    }

    private async Task<Guid> CreateOrderAsync(string productName, int quantity)
    {
        var productId = await GetActiveProductIdAsync(productName);

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
        var newEtag = addResponse.Headers.ETag!.Tag;

        var orderRequest = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        orderRequest.Headers.Add("X-Test-User", Alice);
        orderRequest.Headers.Add("Idempotency-Key", $"consume-reserved-{Guid.NewGuid():N}"[..32]);
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

    private async Task SetReservedAsync(Guid productId, int reserved)
    {
        await using var context = CreateContext();
        await context.Products
            .Where(product => product.Id == productId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(product => product.Reserved, reserved));
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options);

    private sealed record StockSnapshot(int Available, int Reserved);
}

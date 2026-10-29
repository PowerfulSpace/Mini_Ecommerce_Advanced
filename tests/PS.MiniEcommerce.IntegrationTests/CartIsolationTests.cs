using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.IntegrationTests.Fixtures;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class CartIsolationTests : IAsyncLifetime
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const string WirelessMouse = "Wireless Mouse";
    private const string ErgonomicKeyboard = "Ergonomic Keyboard";

    private readonly TestDatabaseFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CartIsolationTests(TestDatabaseFixture fixture)
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
    public async Task AliceAndBobCarts_AreIsolatedAndHaveIndependentVersions()
    {
        var mouseId = await GetProductIdByNameAsync(WirelessMouse);
        var keyboardId = await GetProductIdByNameAsync(ErgonomicKeyboard);

        var aliceEmptyCart = await GetCartAsync(Alice);
        var aliceAdd = await PostItemAsync(Alice, mouseId, quantity: 1, aliceEmptyCart.ETag);
        var aliceETag = aliceAdd.ETag;

        var bobEmptyCart = await GetCartAsync(Bob);
        Assert.Empty(bobEmptyCart.Items);
        Assert.DoesNotContain(bobEmptyCart.Items, item => item.ProductId == mouseId);
        Assert.NotEqual(aliceEmptyCart.ETag, bobEmptyCart.ETag);

        var bobAdd = await PostItemAsync(Bob, keyboardId, quantity: 1, bobEmptyCart.ETag);
        var bobETag = bobAdd.ETag;

        var aliceCart = await GetCartAsync(Alice);
        Assert.Equal(aliceETag, aliceCart.ETag);
        var aliceItem = Assert.Single(aliceCart.Items);
        Assert.Equal(mouseId, aliceItem.ProductId);
        Assert.Equal(1, aliceItem.Quantity);
        Assert.DoesNotContain(aliceCart.Items, item => item.ProductId == keyboardId);

        var bobCart = await GetCartAsync(Bob);
        Assert.Equal(bobETag, bobCart.ETag);
        var bobItem = Assert.Single(bobCart.Items);
        Assert.Equal(keyboardId, bobItem.ProductId);
        Assert.Equal(1, bobItem.Quantity);
        Assert.DoesNotContain(bobCart.Items, item => item.ProductId == mouseId);

        Assert.NotEqual(aliceETag, bobETag);
        Assert.NotEqual(aliceCart.ETag, bobCart.ETag);
    }

    private async Task<CartSnapshot> GetCartAsync(string testUser)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/cart");
        request.Headers.Add("X-Test-User", testUser);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var cart = await response.Content.ReadFromJsonAsync<CartResponse>();
        Assert.NotNull(cart);
        return new CartSnapshot(response.Headers.ETag!.Tag, cart.Items);
    }

    private async Task<CartSnapshot> PostItemAsync(
        string testUser,
        Guid productId,
        int quantity,
        string ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/cart/items")
        {
            Content = JsonContent.Create(new { productId, quantity })
        };
        request.Headers.Add("X-Test-User", testUser);
        request.Headers.Add("If-Match", ifMatch);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var cart = await response.Content.ReadFromJsonAsync<CartResponse>();
        Assert.NotNull(cart);
        return new CartSnapshot(response.Headers.ETag!.Tag, cart.Items);
    }

    private async Task<Guid> GetProductIdByNameAsync(string name)
    {
        await using var context = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(_fixture.ConnectionString)
                .Options);

        return await context.Products
            .Where(product => product.Name == name)
            .Select(product => product.Id)
            .FirstAsync();
    }

    private sealed record CartSnapshot(string ETag, List<CartItemResponse> Items);

    private sealed record CartResponse(List<CartItemResponse> Items);

    private sealed record CartItemResponse(
        Guid ProductId,
        string ProductName,
        decimal UnitPrice,
        int Quantity,
        decimal LineTotal,
        bool IsSelected,
        bool IsAvailable);
}

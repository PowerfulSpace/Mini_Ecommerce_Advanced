using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.IntegrationTests.Fixtures;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class CartConcurrencyTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CartConcurrencyTests(TestDatabaseFixture fixture)
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
    public async Task PostCartItem_TwoConcurrentRequestsWithSameETag_OneSucceedsOneConflicts()
    {
        var etag = await EnsureCartExistsAsync();
        var productId = await GetProductIdByNameAsync("Wireless Mouse");

        var tasks = new[]
        {
            Task.Run(() => PostItemAsync(productId, 1, etag)),
            Task.Run(() => PostItemAsync(productId, 1, etag))
        };

        var responses = await Task.WhenAll(tasks);
        var statuses = responses.Select(response => response.StatusCode).ToList();

        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.OK));
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.PreconditionFailed));
    }

    [Fact]
    public async Task PostCartItem_ConcurrentWithDifferentProducts_BothSucceedOrSerialize()
    {
        var etag = await EnsureCartExistsAsync();
        var firstProductId = await GetProductIdByNameAsync("Wireless Mouse");
        var secondProductId = await GetProductIdByNameAsync("Ergonomic Keyboard");

        var tasks = new[]
        {
            Task.Run(() => PostItemAsync(firstProductId, 1, etag)),
            Task.Run(() => PostItemAsync(secondProductId, 1, etag))
        };

        var responses = await Task.WhenAll(tasks);
        var statuses = responses.Select(response => response.StatusCode).ToList();

        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.OK));
        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.PreconditionFailed));
    }

    private async Task<string> EnsureCartExistsAsync()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/cart");
        request.Headers.Add("X-Test-User", "alice");

        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return response.Headers.ETag!.ToString();
    }

    private Task<HttpResponseMessage> PostItemAsync(Guid productId, int quantity, string ifMatch)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/cart/items");
        request.Headers.Add("X-Test-User", "alice");
        request.Headers.Add("If-Match", ifMatch);
        request.Content = JsonContent.Create(new { productId, quantity });
        return _client.SendAsync(request);
    }

    private async Task<Guid> GetProductIdByNameAsync(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options;

        await using var context = new AppDbContext(options);
        return await context.Products
            .Where(product => product.Name == name)
            .Select(product => product.Id)
            .FirstAsync();
    }
}

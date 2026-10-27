using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.IntegrationTests.Fixtures;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class CartMutationEndpointTests : IAsyncLifetime
{
    private const string Alice = "alice";

    private readonly TestDatabaseFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CartMutationEndpointTests(TestDatabaseFixture fixture)
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
    public async Task PostCartItem_WithoutIfMatch_Returns428()
    {
        await EnsureCartExistsAsync();
        var productId = await GetProductIdByNameAsync("Wireless Mouse");

        var response = await PostItemAsync(productId, 1, ifMatch: null);

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("precondition_required", error.Code);
    }

    [Fact]
    public async Task PostCartItem_WithValidIfMatch_Returns200AndNewETag()
    {
        var etag = await EnsureCartExistsAsync();
        var productId = await GetProductIdByNameAsync("Wireless Mouse");

        var response = await PostItemAsync(productId, 2, etag);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var newETag = response.Headers.ETag!.ToString();
        Assert.NotEqual(etag, newETag);

        var cart = await response.Content.ReadFromJsonAsync<CartResponse>();
        Assert.NotNull(cart);
        var item = Assert.Single(cart.Items);
        Assert.Equal(productId, item.ProductId);
        Assert.Equal(2, item.Quantity);
    }

    [Fact]
    public async Task PostCartItem_WithStaleETag_Returns412()
    {
        var firstETag = await EnsureCartExistsAsync();
        var productId = await GetProductIdByNameAsync("Wireless Mouse");

        var first = await PostItemAsync(productId, 1, firstETag);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await PostItemAsync(productId, 1, firstETag);

        Assert.Equal(HttpStatusCode.PreconditionFailed, second.StatusCode);

        var error = await second.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("cart_version_conflict", error.Code);
    }

    [Fact]
    public async Task PostCartItem_WithWildcardIfMatch_Returns400()
    {
        await EnsureCartExistsAsync();
        var productId = await GetProductIdByNameAsync("Wireless Mouse");

        var response = await PostItemAsync(productId, 1, "*");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("validation_error", error.Code);
    }

    [Fact]
    public async Task PostCartItem_WithInactiveProduct_Returns409ProductUnavailable()
    {
        var etag = await EnsureCartExistsAsync();
        var inactiveProductId = await GetInactiveProductIdAsync();

        var response = await PostItemAsync(inactiveProductId, 1, etag);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("product_unavailable", error.Code);
    }

    [Fact]
    public async Task PostCartItem_WithInsufficientStock_Returns409InsufficientStock()
    {
        var etag = await EnsureCartExistsAsync();
        var lowStockProductId = await GetProductIdByNameAsync("Limited Edition Mug");

        var response = await PostItemAsync(lowStockProductId, 2, etag);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("insufficient_stock", error.Code);
    }

    [Fact]
    public async Task PostCartItem_WithUnknownProduct_Returns404ProductNotFound()
    {
        var etag = await EnsureCartExistsAsync();

        var response = await PostItemAsync(Guid.NewGuid(), 1, etag);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("product_not_found", error.Code);
    }

    [Fact]
    public async Task PutCartItem_WithoutIfMatch_Returns428()
    {
        await EnsureCartExistsAsync();
        var productId = await GetProductIdByNameAsync("Wireless Mouse");

        var response = await PutItemAsync(productId, 1, ifMatch: null);

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("precondition_required", error.Code);
    }

    [Fact]
    public async Task PutCartItem_WithZeroQuantity_Returns400()
    {
        var etag = await EnsureCartExistsAsync();

        var response = await PutItemAsync(Guid.NewGuid(), 0, etag);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("validation_error", error.Code);
    }

    [Fact]
    public async Task PutCartItem_WhenItemDoesNotExist_Returns404CartItemNotFound()
    {
        var etag = await EnsureCartExistsAsync();

        var response = await PutItemAsync(Guid.NewGuid(), 1, etag);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("cart_item_not_found", error.Code);
    }

    [Fact]
    public async Task DeleteCartItem_WithoutIfMatch_Returns428()
    {
        await EnsureCartExistsAsync();

        var response = await DeleteItemAsync(Guid.NewGuid(), ifMatch: null);

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("precondition_required", error.Code);
    }

    [Fact]
    public async Task DeleteCartItem_WhenItemDoesNotExist_Returns404CartItemNotFound()
    {
        var etag = await EnsureCartExistsAsync();

        var response = await DeleteItemAsync(Guid.NewGuid(), etag);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("cart_item_not_found", error.Code);
    }

    [Fact]
    public async Task PutSelection_WithoutIfMatch_Returns428()
    {
        await EnsureCartExistsAsync();
        var productId = await GetProductIdByNameAsync("Wireless Mouse");

        var response = await PutSelectionAsync(
            new object[] { new { productId, isSelected = false } },
            ifMatch: null);

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("precondition_required", error.Code);
    }

    [Fact]
    public async Task PutSelection_WithValidIfMatch_UpdatesSelectionAndTotals()
    {
        var etag = await EnsureCartExistsAsync();
        var mouseId = await GetProductIdByNameAsync("Wireless Mouse");
        var keyboardId = await GetProductIdByNameAsync("Ergonomic Keyboard");

        var addMouse = await PostItemAsync(mouseId, 2, etag);
        Assert.Equal(HttpStatusCode.OK, addMouse.StatusCode);
        var addKeyboard = await PostItemAsync(keyboardId, 1, addMouse.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, addKeyboard.StatusCode);

        var oldETag = addKeyboard.Headers.ETag!.ToString();
        var response = await PutSelectionAsync(
            new object[] { new { productId = mouseId, isSelected = false } },
            oldETag);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var newETag = response.Headers.ETag!.ToString();
        Assert.NotEqual(oldETag, newETag);

        var cart = await response.Content.ReadFromJsonAsync<CartResponse>();
        Assert.NotNull(cart);
        Assert.Equal(49.48m, cart.TotalAmount);
        Assert.Equal(9.50m, cart.SelectedTotalAmount);

        var mouse = cart.Items.Single(item => item.ProductId == mouseId);
        var keyboard = cart.Items.Single(item => item.ProductId == keyboardId);
        Assert.False(mouse.IsSelected);
        Assert.True(keyboard.IsSelected);

        var getResponse = await _client.SendAsync(
            CreateAuthenticatedRequest(HttpMethod.Get, "/api/cart"));
        getResponse.EnsureSuccessStatusCode();
        var fetchedCart = await getResponse.Content.ReadFromJsonAsync<CartResponse>();
        Assert.NotNull(fetchedCart);
        Assert.Equal(newETag, getResponse.Headers.ETag!.ToString());
        Assert.Equal(9.50m, fetchedCart.SelectedTotalAmount);
        Assert.False(fetchedCart.Items.Single(item => item.ProductId == mouseId).IsSelected);
    }

    [Fact]
    public async Task PutSelection_WithStaleETag_Returns412()
    {
        var firstETag = await EnsureCartExistsAsync();
        var productId = await GetProductIdByNameAsync("Wireless Mouse");
        var add = await PostItemAsync(productId, 1, firstETag);
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);

        var response = await PutSelectionAsync(
            new object[] { new { productId, isSelected = false } },
            firstETag);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("cart_version_conflict", error.Code);
    }

    [Fact]
    public async Task PutSelection_WithEmptyItems_Returns400()
    {
        var etag = await EnsureCartExistsAsync();

        var response = await PutSelectionAsync(Array.Empty<object>(), etag);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("validation_error", error.Code);
    }

    [Fact]
    public async Task PutSelection_WithUnknownProduct_Returns404CartItemNotFound()
    {
        var etag = await EnsureCartExistsAsync();

        var response = await PutSelectionAsync(
            new object[] { new { productId = Guid.NewGuid(), isSelected = false } },
            etag);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("cart_item_not_found", error.Code);
    }

    private async Task<string> EnsureCartExistsAsync()
    {
        var response = await _client.SendAsync(CreateAuthenticatedRequest(HttpMethod.Get, "/api/cart"));
        response.EnsureSuccessStatusCode();
        return response.Headers.ETag!.ToString();
    }

    private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string url, string? ifMatch = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Test-User", Alice);
        if (ifMatch is not null)
        {
            request.Headers.Add("If-Match", ifMatch);
        }

        return request;
    }

    private Task<HttpResponseMessage> PostItemAsync(Guid productId, int quantity, string? ifMatch)
    {
        var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/cart/items", ifMatch);
        request.Content = JsonContent.Create(new { productId, quantity });
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PutItemAsync(Guid productId, int quantity, string? ifMatch)
    {
        var request = CreateAuthenticatedRequest(HttpMethod.Put, $"/api/cart/items/{productId}", ifMatch);
        request.Content = JsonContent.Create(new { quantity });
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> DeleteItemAsync(Guid productId, string? ifMatch)
    {
        var request = CreateAuthenticatedRequest(HttpMethod.Delete, $"/api/cart/items/{productId}", ifMatch);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PutSelectionAsync(object[] items, string? ifMatch)
    {
        var request = CreateAuthenticatedRequest(HttpMethod.Put, "/api/cart/selection", ifMatch);
        request.Content = JsonContent.Create(new { items });
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

    private async Task<Guid> GetInactiveProductIdAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options;

        await using var context = new AppDbContext(options);
        return await context.Products
            .Where(product => !product.IsActive)
            .Select(product => product.Id)
            .FirstAsync();
    }

    private sealed record CartResponse(
        List<CartItemResponse> Items,
        decimal TotalAmount,
        decimal SelectedTotalAmount);

    private sealed record CartItemResponse(
        Guid ProductId,
        string ProductName,
        decimal UnitPrice,
        int Quantity,
        decimal LineTotal,
        bool IsSelected,
        bool IsAvailable);

    private sealed record ErrorResponse(string Code);
}

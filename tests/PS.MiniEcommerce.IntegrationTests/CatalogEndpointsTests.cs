using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.IntegrationTests.Fixtures;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class CatalogEndpointsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CatalogEndpointsTests(TestDatabaseFixture fixture)
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
    public async Task GetProducts_ReturnsOnlyActiveProducts_SortedByName()
    {
        var response = await _client.GetAsync("/api/products");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<PagedResponse>();
        Assert.NotNull(body);

        Assert.Equal(8, body.TotalCount);
        Assert.Equal(8, body.Items.Count);

        var names = body.Items.Select(item => item.Name).ToList();
        Assert.Equal(names.OrderBy(name => name, StringComparer.Ordinal).ToList(), names);
        Assert.DoesNotContain(body.Items, item => item.Name == "Discontinued Tablet Case");
    }

    [Fact]
    public async Task GetProducts_WithPagination_ReturnsRequestedPage()
    {
        var response = await _client.GetAsync("/api/products?page=1&pageSize=3");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<PagedResponse>();
        Assert.NotNull(body);

        Assert.Equal(3, body.Items.Count);
        Assert.Equal(1, body.Page);
        Assert.Equal(3, body.PageSize);
        Assert.Equal(8, body.TotalCount);
    }

    [Fact]
    public async Task GetProducts_WithPageSizeAboveMaximum_Returns400()
    {
        var response = await _client.GetAsync("/api/products?pageSize=101");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("validation_error", error.Code);
    }

    [Fact]
    public async Task GetProducts_WithPageZero_Returns400()
    {
        var response = await _client.GetAsync("/api/products?page=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("validation_error", error.Code);
    }

    [Fact]
    public async Task GetProductById_WithExistingActiveId_ReturnsProduct()
    {
        var list = await (await _client.GetAsync("/api/products"))
            .Content.ReadFromJsonAsync<PagedResponse>();
        Assert.NotNull(list);

        var expected = list.Items[0];

        var response = await _client.GetAsync($"/api/products/{expected.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var product = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.NotNull(product);
        Assert.Equal(expected.Id, product.Id);
        Assert.Equal(expected.Name, product.Name);
        Assert.Equal(expected.Description, product.Description);
        Assert.Equal(expected.Price, product.Price);
        Assert.Equal(expected.StockQuantity, product.StockQuantity);
    }

    [Fact]
    public async Task GetProductById_WithUnknownId_Returns404WithProductNotFoundCode()
    {
        var response = await _client.GetAsync($"/api/products/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("product_not_found", error.Code);
    }

    [Fact]
    public async Task GetProductById_WithInactiveProduct_Returns404()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options;

        Guid inactiveProductId;
        await using (var context = new AppDbContext(options))
        {
            inactiveProductId = await context.Products
                .Where(product => !product.IsActive)
                .Select(product => product.Id)
                .FirstAsync();
        }

        var response = await _client.GetAsync($"/api/products/{inactiveProductId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("product_not_found", error.Code);
    }

    private sealed record PagedResponse(List<ProductResponse> Items, int Page, int PageSize, int TotalCount);

    private sealed record ProductResponse(Guid Id, string Name, string Description, decimal Price, int StockQuantity);

    private sealed record ErrorResponse(string Code);
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using PS.MiniEcommerce.IntegrationTests.Fixtures;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class ModelBindingErrorTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ModelBindingErrorTests(TestDatabaseFixture fixture)
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
    public async Task PostCartItems_WithEmptyBody_Returns400ProblemDetailsWithValidationErrorCode()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/cart/items")
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("X-Test-User", "alice");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("validation_error", error.Code);
        Assert.NotNull(error.Errors);
        Assert.NotEmpty(error.Errors);
    }

    [Fact]
    public async Task GetProducts_WithNonIntegerPage_Returns400ProblemDetailsWithValidationErrorCode()
    {
        var response = await _client.GetAsync("/api/products?page=abc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("validation_error", error.Code);
        Assert.NotNull(error.Errors);
    }

    private sealed record ErrorResponse(string Code, Dictionary<string, string[]>? Errors);
}

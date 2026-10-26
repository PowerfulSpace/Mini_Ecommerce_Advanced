using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PS.MiniEcommerce.IntegrationTests.Fixtures;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class HealthEndpointTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public HealthEndpointTests(TestDatabaseFixture fixture)
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
    public async Task GetHealth_WhenDatabaseReachable_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("Healthy", body.Status);
    }

    [Fact]
    public async Task GetHealth_WhenDatabaseUnavailable_ReturnsProblemDetails()
    {
        var databaseName = $"MiniEcommerce_Advanced_Test_Unavailable_{Guid.NewGuid():N}";
        var unavailableConnectionString =
            $"Server=localhost;Database={databaseName};Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=2;";

        using var factory = new CustomWebApplicationFactory(unavailableConnectionString, "Production");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = problem.RootElement;
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.6.4", root.GetProperty("type").GetString());
        Assert.Equal("Service unavailable.", root.GetProperty("title").GetString());
        Assert.Equal(503, root.GetProperty("status").GetInt32());
        Assert.Equal("The database is not reachable.", root.GetProperty("detail").GetString());
        Assert.Equal("/health", root.GetProperty("instance").GetString());
        Assert.Equal("health_unavailable", root.GetProperty("code").GetString());
    }

    private sealed record HealthResponse(string Status);
}

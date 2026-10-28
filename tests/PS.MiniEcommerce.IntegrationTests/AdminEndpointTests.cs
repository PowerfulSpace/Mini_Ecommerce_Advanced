using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.Infrastructure.Seed;
using PS.MiniEcommerce.IntegrationTests.Fixtures;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class AdminEndpointTests : IAsyncLifetime
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const string Admin = "admin";
    private const string WirelessMouse = "Wireless Mouse";

    private readonly TestDatabaseFixture _fixture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminEndpointTests(TestDatabaseFixture fixture)
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

    [Theory]
    [InlineData(Alice)]
    [InlineData(Bob)]
    public async Task NonAdmin_CannotDeactivateProduct(string user)
    {
        var productId = await GetActiveProductIdAsync();

        var response = await SendAdminAsync(
            HttpMethod.Post,
            $"/api/admin/products/{productId}/deactivate",
            user);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("forbidden", error.Code);
    }

    [Theory]
    [InlineData(Alice)]
    [InlineData(Bob)]
    public async Task NonAdmin_CannotUpdateStock(string user)
    {
        var productId = await GetActiveProductIdAsync();

        var response = await SendAdminAsync(
            HttpMethod.Post,
            $"/api/admin/products/{productId}/stock",
            user,
            new { available = 5 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("forbidden", error.Code);
    }

    [Fact]
    public async Task Admin_CanDeactivateProductAndAuditUsesAdminActor()
    {
        var productId = await GetActiveProductIdAsync();
        var adminUserId = await GetUserIdAsync(SeedData.AdminEmail);

        var response = await SendAdminAsync(
            HttpMethod.Post,
            $"/api/admin/products/{productId}/deactivate",
            Admin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var product = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.NotNull(product);
        Assert.Equal(productId, product.Id);

        await using var context = CreateContext();
        var isActive = await context.Products
            .Where(item => item.Id == productId)
            .Select(item => item.IsActive)
            .SingleAsync();
        Assert.False(isActive);

        var auditLog = await context.AuditLogs
            .AsNoTracking()
            .SingleAsync(log => log.Action == "ProductDeactivated");
        Assert.Equal("Product", auditLog.EntityType);
        Assert.Equal(productId, auditLog.EntityId);
        Assert.Equal(adminUserId, auditLog.ActorUserId);
        Assert.Null(auditLog.Payload);
    }

    [Fact]
    public async Task RepeatedDeactivate_IsIdempotentAndWritesOneAuditLog()
    {
        var productId = await GetActiveProductIdAsync();

        var first = await SendAdminAsync(
            HttpMethod.Post,
            $"/api/admin/products/{productId}/deactivate",
            Admin);
        var second = await SendAdminAsync(
            HttpMethod.Post,
            $"/api/admin/products/{productId}/deactivate",
            Admin);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        await using var context = CreateContext();
        Assert.Equal(
            1,
            await context.AuditLogs.CountAsync(log => log.Action == "ProductDeactivated"));
    }

    [Fact]
    public async Task Admin_CanUpdateStockWithoutChangingReserved()
    {
        var productId = await GetActiveProductIdAsync();
        var adminUserId = await GetUserIdAsync(SeedData.AdminEmail);

        await using (var context = CreateContext())
        {
            await context.Products
                .Where(product => product.Id == productId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(product => product.Reserved, 3));
        }

        var response = await SendAdminAsync(
            HttpMethod.Post,
            $"/api/admin/products/{productId}/stock",
            Admin,
            new { available = 7 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var product = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.NotNull(product);
        Assert.Equal(7, product.Available);

        await using (var context = CreateContext())
        {
            var stored = await context.Products
                .AsNoTracking()
                .SingleAsync(item => item.Id == productId);
            Assert.Equal(7, stored.Available);
            Assert.Equal(3, stored.Reserved);

            var auditLog = await context.AuditLogs
                .AsNoTracking()
                .SingleAsync(log => log.Action == "StockAdjusted");
            Assert.Equal(adminUserId, auditLog.ActorUserId);

            using var payload = JsonDocument.Parse(auditLog.Payload!);
            Assert.Equal(7, payload.RootElement.GetProperty("newAvailable").GetInt32());
        }
    }

    [Fact]
    public async Task AdminStock_WithNegativeAvailable_ReturnsValidationError()
    {
        var productId = await GetActiveProductIdAsync();

        var response = await SendAdminAsync(
            HttpMethod.Post,
            $"/api/admin/products/{productId}/stock",
            Admin,
            new { available = -1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("validation_error", error.Code);
    }

    [Fact]
    public async Task AdminStock_WhenRepositoryReportsConflict_ReturnsAdminStockConflict()
    {
        var productId = await GetActiveProductIdAsync();
        var repository = Substitute.For<IProductRepository>();
        repository
            .AdjustAvailableAsync(
                Arg.Any<Guid>(),
                Arg.Any<int>(),
                Arg.Any<DateTime>(),
                Arg.Any<CancellationToken>())
            .Returns((
                AdminStockUpdateResult.Conflict,
                (Product?)null,
                (IReadOnlyList<object>)Array.Empty<object>()));

        using var factory = new CustomWebApplicationFactory(
            _fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.RemoveAll<IProductRepository>();
                services.AddScoped(_ => repository);
            });
        using var client = factory.CreateClient();

        var response = await SendAdminAsync(
            HttpMethod.Post,
            $"/api/admin/products/{productId}/stock",
            Admin,
            new { available = 5 },
            client);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("admin_stock_conflict", error.Code);
    }

    [Fact]
    public async Task AdminEndpoints_AreNotFoundInProduction()
    {
        using var factory = new CustomWebApplicationFactory(
            _fixture.ConnectionString,
            environment: "Production");
        using var client = factory.CreateClient();
        var productId = await GetActiveProductIdAsync();

        var response = await SendAdminAsync(
            HttpMethod.Post,
            $"/api/admin/products/{productId}/deactivate",
            Admin,
            client: client);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Seed_CreatesAdminAndCustomerRolesIdempotently()
    {
        await using (var context = CreateContext())
        {
            var users = await context.Users
                .AsNoTracking()
                .ToListAsync();

            var admin = Assert.Single(users, user => user.Email == SeedData.AdminEmail);
            Assert.Equal(UserRole.Admin, admin.Role);

            var alice = Assert.Single(users, user => user.Email == SeedData.AliceEmail);
            Assert.Equal(UserRole.Customer, alice.Role);

            var bob = Assert.Single(users, user => user.Email == SeedData.BobEmail);
            Assert.Equal(UserRole.Customer, bob.Role);

            var seeder = new DataSeeder(context);
            await seeder.SeedAsync();

            Assert.Equal(1, await context.Users.CountAsync(user => user.Email == SeedData.AdminEmail));
        }
    }

    [Fact]
    public async Task Seed_DoesNotWriteProductDeactivatedAudit()
    {
        await using var context = CreateContext();

        Assert.Equal(0, await context.AuditLogs.CountAsync());
    }

    private async Task<HttpResponseMessage> SendAdminAsync(
        HttpMethod method,
        string path,
        string user,
        object? body = null,
        HttpClient? client = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-User", user);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await (client ?? _client).SendAsync(request);
    }

    private async Task<Guid> GetActiveProductIdAsync()
    {
        await using var context = CreateContext();
        return await context.Products
            .Where(product => product.Name == WirelessMouse && product.IsActive)
            .Select(product => product.Id)
            .FirstAsync();
    }

    private async Task<Guid> GetUserIdAsync(string email)
    {
        await using var context = CreateContext();
        return await context.Users
            .Where(user => user.Email == email)
            .Select(user => user.Id)
            .SingleAsync();
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options);

    private sealed record ErrorResponse(string Code);

    private sealed record ProductResponse(Guid Id, string Name, decimal Price, int Available);
}

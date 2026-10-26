using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.Infrastructure.Seed;
using Respawn;
using Respawn.Graph;

namespace PS.MiniEcommerce.IntegrationTests.Fixtures;

public sealed class TestDatabaseFixture : IAsyncLifetime
{
    private Respawner _respawner = null!;
    private bool _initialized;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        ConnectionString = Environment.GetEnvironmentVariable("MINIECOMMERCE_TEST_DB")
            ?? throw new InvalidOperationException(
                "Set MINIECOMMERCE_TEST_DB environment variable to a SQL Server connection string for integration tests.");

        await using var context = new AppDbContext(CreateOptions());

        var connection = context.Database.GetDbConnection();

        var databaseName = connection.Database;
        if (string.IsNullOrWhiteSpace(databaseName)
            || !databaseName.Contains("_Test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "MINIECOMMERCE_TEST_DB must point to a database whose name contains '_Test'.");
        }

        await context.Database.MigrateAsync();

        await connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            TablesToIgnore = new[] { new Table("__EFMigrationsHistory") },
            DbAdapter = DbAdapter.SqlServer
        });

        _initialized = true;
    }

    public async Task ResetAsync()
    {
        await using (var context = new AppDbContext(CreateOptions()))
        {
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync();

            await _respawner.ResetAsync(connection);
        }

        await using (var context = new AppDbContext(CreateOptions()))
        {
            var seeder = new DataSeeder(context);
            await seeder.SeedAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (!_initialized)
        {
            return;
        }

        await using var context = new AppDbContext(CreateOptions());
        await context.Database.EnsureDeletedAsync();
    }

    private DbContextOptions<AppDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
}

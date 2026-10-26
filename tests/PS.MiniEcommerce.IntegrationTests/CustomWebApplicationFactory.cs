using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PS.MiniEcommerce.Infrastructure;

namespace PS.MiniEcommerce.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly string _environment;
    private readonly ISaveChangesInterceptor? _saveChangesInterceptor;

    public CustomWebApplicationFactory(
        string connectionString,
        string environment = "Development",
        ISaveChangesInterceptor? saveChangesInterceptor = null)
    {
        _connectionString = connectionString;
        _environment = environment;
        _saveChangesInterceptor = saveChangesInterceptor;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.UseSetting("ConnectionStrings:Default", _connectionString);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlServer(_connectionString);

                if (_saveChangesInterceptor is not null)
                {
                    options.AddInterceptors(_saveChangesInterceptor);
                }
            });
        });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Checkout;
using PS.MiniEcommerce.Infrastructure.Checkout;
using PS.MiniEcommerce.Infrastructure.Persistence;
using PS.MiniEcommerce.Infrastructure.Seed;

namespace PS.MiniEcommerce.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<DataSeeder>();

        services.AddScoped<IProductRepository, ProductRepository>();

        services.AddScoped<ICartRepository, CartRepository>();

        services.AddScoped<IOrderRepository, OrderRepository>();

        services.AddScoped<ICheckoutAttemptRepository, CheckoutAttemptRepository>();

        services.AddScoped<ICheckoutTransactionExecutor, CheckoutTransactionExecutor>();

        return services;
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PS.MiniEcommerce.Application.Abstractions.Outbox;
using PS.MiniEcommerce.Application.Abstractions.Payments;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Checkout;
using PS.MiniEcommerce.Application.Notifications;
using PS.MiniEcommerce.Application.Payments;
using PS.MiniEcommerce.Infrastructure.Checkout;
using PS.MiniEcommerce.Infrastructure.Notifications;
using PS.MiniEcommerce.Infrastructure.Outbox;
using PS.MiniEcommerce.Infrastructure.Payments;
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

        services.AddScoped<IPaymentRepository, PaymentRepository>();

        services.AddScoped<ICheckoutAttemptRepository, CheckoutAttemptRepository>();

        services.AddScoped<IPaymentAttemptRepository, PaymentAttemptRepository>();

        services.AddScoped<IAuditLogRepository, AuditLogRepository>();

        services.AddScoped<IOutboxRepository, OutboxRepository>();

        services.AddSingleton<INotificationStore, InMemoryNotificationStore>();

        services.AddScoped<IOutboxSubscriber, NotificationSubscriber>();

        services.AddScoped<IPaymentProvider, MockPaymentProvider>();

        services.AddScoped<ICheckoutTransactionExecutor, CheckoutTransactionExecutor>();

        services.AddScoped<IConfirmTransactionExecutor, ConfirmTransactionExecutor>();

        services.AddScoped<IPaymentRefundService, PaymentRefundService>();

        services.AddScoped<IRefundTransactionExecutor, RefundTransactionExecutor>();

        services.AddHostedService<OutboxDispatcher>();

        return services;
    }
}

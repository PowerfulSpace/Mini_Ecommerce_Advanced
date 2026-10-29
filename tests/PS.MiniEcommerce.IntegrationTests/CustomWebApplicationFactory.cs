using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PS.MiniEcommerce.Application.Abstractions.Outbox;
using PS.MiniEcommerce.Application.Events;
using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.Infrastructure.Outbox;

namespace PS.MiniEcommerce.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly string _environment;
    private readonly ISaveChangesInterceptor? _saveChangesInterceptor;
    private readonly NotificationCapture? _notificationCapture;
    private readonly OutboxCapture? _outboxCapture;
    private readonly bool _enableOutboxDispatcher;
    private readonly Action<IServiceCollection>? _configureTestServices;

    public CustomWebApplicationFactory(
        string connectionString,
        string environment = "Development",
        ISaveChangesInterceptor? saveChangesInterceptor = null,
        NotificationCapture? notificationCapture = null,
        Action<IServiceCollection>? configureTestServices = null,
        OutboxCapture? outboxCapture = null,
        bool enableOutboxDispatcher = false)
    {
        _connectionString = connectionString;
        _environment = environment;
        _saveChangesInterceptor = saveChangesInterceptor;
        _notificationCapture = notificationCapture;
        _outboxCapture = outboxCapture;
        _enableOutboxDispatcher = enableOutboxDispatcher;
        _configureTestServices = configureTestServices;
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

        builder.ConfigureTestServices(services =>
        {
            if (_notificationCapture is not null)
            {
                services.AddSingleton(_notificationCapture);
                AddCaptureHandlers(services);
            }

            if (_outboxCapture is not null)
            {
                services.AddSingleton(_outboxCapture);
                services.AddSingleton<IOutboxSubscriber, TestOutboxSubscriber>();
            }

            if (!_enableOutboxDispatcher)
            {
                var dispatcher = services.FirstOrDefault(
                    descriptor => descriptor.ServiceType == typeof(IHostedService)
                        && descriptor.ImplementationType == typeof(OutboxDispatcher));

                if (dispatcher is not null)
                {
                    services.Remove(dispatcher);
                }
            }

            _configureTestServices?.Invoke(services);
        });
    }

    private static void AddCaptureHandlers(IServiceCollection services)
    {
        services.AddSingleton<
            INotificationHandler<DomainEventNotification<object>>,
            CaptureNotificationHandler<object>>();
        services.AddSingleton<
            INotificationHandler<DomainEventNotification<OrderPlaced>>,
            CaptureNotificationHandler<OrderPlaced>>();
        services.AddSingleton<
            INotificationHandler<DomainEventNotification<OrderPaid>>,
            CaptureNotificationHandler<OrderPaid>>();
        services.AddSingleton<
            INotificationHandler<DomainEventNotification<OrderShipped>>,
            CaptureNotificationHandler<OrderShipped>>();
        services.AddSingleton<
            INotificationHandler<DomainEventNotification<OrderDelivered>>,
            CaptureNotificationHandler<OrderDelivered>>();
        services.AddSingleton<
            INotificationHandler<DomainEventNotification<OrderCancelled>>,
            CaptureNotificationHandler<OrderCancelled>>();
        services.AddSingleton<
            INotificationHandler<DomainEventNotification<OrderReturned>>,
            CaptureNotificationHandler<OrderReturned>>();
        services.AddSingleton<
            INotificationHandler<DomainEventNotification<PaymentConfirmed>>,
            CaptureNotificationHandler<PaymentConfirmed>>();
        services.AddSingleton<
            INotificationHandler<DomainEventNotification<PaymentRefunded>>,
            CaptureNotificationHandler<PaymentRefunded>>();
        services.AddSingleton<
            INotificationHandler<DomainEventNotification<StockAdjusted>>,
            CaptureNotificationHandler<StockAdjusted>>();
        services.AddSingleton<
            INotificationHandler<DomainEventNotification<ProductDeactivated>>,
            CaptureNotificationHandler<ProductDeactivated>>();
    }
}

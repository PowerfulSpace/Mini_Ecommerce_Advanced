using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Domain.Payments;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.Infrastructure.Persistence;
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
    private readonly NotificationCapture _capture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminEndpointTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
        _capture = new NotificationCapture();
        _factory = new CustomWebApplicationFactory(
            fixture.ConnectionString,
            notificationCapture: _capture);
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
    public async Task Admin_CanShipPaidOrderAndAuditUsesAdminActor()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 2);
        var stockAfterCheckout = await GetStockAsync(WirelessMouse);
        var adminUserId = await GetUserIdAsync(SeedData.AdminEmail);
        await SetStatusAsync(orderId, OrderStatus.Paid);
        var etagBefore = await GetOrderETagAsync(Alice, orderId);

        var response = await SendAdminAsync(
            HttpMethod.Post,
            $"/api/admin/orders/{orderId}/ship",
            Admin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        Assert.NotEqual(etagBefore, response.Headers.ETag!.Tag);
        var order = await response.Content.ReadFromJsonAsync<OrderDetailResponse>();
        Assert.NotNull(order);
        Assert.Equal(orderId, order.Id);
        Assert.Equal("Shipped", order.Status);

        Assert.Equal(OrderStatus.Shipped, await GetOrderStatusAsync(orderId));
        Assert.Equal(stockAfterCheckout, await GetStockAsync(WirelessMouse));

        var auditLog = await GetAuditLogAsync("OrderShipped");
        Assert.Equal("Order", auditLog.EntityType);
        Assert.Equal(orderId, auditLog.EntityId);
        Assert.Equal(adminUserId, auditLog.ActorUserId);
        Assert.Null(auditLog.Payload);
    }

    [Fact]
    public async Task AdminShip_WhenOrderIsCreated_Returns409InvalidOrderTransition()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);
        var stockBefore = await GetStockAsync(WirelessMouse);

        var response = await SendAdminAsync(
            HttpMethod.Post,
            $"/api/admin/orders/{orderId}/ship",
            Admin);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("invalid_order_transition", error.Code);

        Assert.Equal(OrderStatus.Created, await GetOrderStatusAsync(orderId));
        Assert.Equal(stockBefore, await GetStockAsync(WirelessMouse));
        await using var context = CreateContext();
        Assert.Equal(0, await context.AuditLogs.CountAsync(log => log.Action == "OrderShipped"));
    }

    [Fact]
    public async Task Admin_CanDeliverShippedOrderAndAuditUsesAdminActor()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);
        var adminUserId = await GetUserIdAsync(SeedData.AdminEmail);
        await SetStatusAsync(orderId, OrderStatus.Shipped);
        var etagBefore = await GetOrderETagAsync(Alice, orderId);

        var response = await SendAdminAsync(
            HttpMethod.Post,
            $"/api/admin/orders/{orderId}/deliver",
            Admin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        Assert.NotEqual(etagBefore, response.Headers.ETag!.Tag);
        var order = await response.Content.ReadFromJsonAsync<OrderDetailResponse>();
        Assert.NotNull(order);
        Assert.Equal(orderId, order.Id);
        Assert.Equal("Delivered", order.Status);

        Assert.Equal(OrderStatus.Delivered, await GetOrderStatusAsync(orderId));

        var auditLog = await GetAuditLogAsync("OrderDelivered");
        Assert.Equal("Order", auditLog.EntityType);
        Assert.Equal(orderId, auditLog.EntityId);
        Assert.Equal(adminUserId, auditLog.ActorUserId);
        Assert.Null(auditLog.Payload);
    }

    [Fact]
    public async Task AdminDeliver_WhenOrderIsPaid_Returns409InvalidOrderTransition()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);
        await SetStatusAsync(orderId, OrderStatus.Paid);

        var response = await SendAdminAsync(
            HttpMethod.Post,
            $"/api/admin/orders/{orderId}/deliver",
            Admin);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("invalid_order_transition", error.Code);

        Assert.Equal(OrderStatus.Paid, await GetOrderStatusAsync(orderId));
    }

    [Fact]
    public async Task Admin_CanReturnDeliveredOrderAndRestoresAvailableWithoutChangingReserved()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 2);
        var stockAfterCheckout = await GetStockAsync(WirelessMouse);
        var adminUserId = await GetUserIdAsync(SeedData.AdminEmail);
        var paymentId = await SetStatusAsync(orderId, OrderStatus.Delivered);
        var etagBefore = await GetOrderETagAsync(Alice, orderId);

        var response = await SendAdminAsync(
            HttpMethod.Post,
            $"/api/admin/orders/{orderId}/return",
            Admin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        Assert.NotEqual(etagBefore, response.Headers.ETag!.Tag);
        var order = await response.Content.ReadFromJsonAsync<OrderDetailResponse>();
        Assert.NotNull(order);
        Assert.Equal(orderId, order.Id);
        Assert.Equal("Returned", order.Status);

        Assert.Equal(OrderStatus.Returned, await GetOrderStatusAsync(orderId));
        Assert.Equal(paymentId, await GetPaymentIdAsync(orderId));

        var stockAfterReturn = await GetStockAsync(WirelessMouse);
        Assert.Equal(stockAfterCheckout.Available + 2, stockAfterReturn.Available);
        Assert.Equal(stockAfterCheckout.Reserved, stockAfterReturn.Reserved);

        var payment = await GetPaymentByOrderIdAsync(orderId);
        Assert.NotNull(payment);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(payment.Amount.Amount, payment.RefundedAmount.Amount);
        Assert.NotNull(payment.RefundedAtUtc);
        Assert.Single(payment.Refunds);

        var paymentRefunded = Assert.Single(_capture.Events.OfType<PaymentRefunded>());
        Assert.Equal(payment.Id, paymentRefunded.PaymentId);
        Assert.Equal(payment.Amount.Amount, paymentRefunded.Amount.Amount);
        Assert.Null(paymentRefunded.Reason);

        var refundAudit = await GetAuditLogAsync("PaymentRefunded");
        Assert.Equal("Payment", refundAudit.EntityType);
        Assert.Equal(payment.Id, refundAudit.EntityId);
        Assert.Equal(adminUserId, refundAudit.ActorUserId);

        var auditLog = await GetAuditLogAsync("OrderReturned");
        Assert.Equal("Order", auditLog.EntityType);
        Assert.Equal(orderId, auditLog.EntityId);
        Assert.Equal(adminUserId, auditLog.ActorUserId);
        Assert.Null(auditLog.Payload);
    }

    [Fact]
    public async Task AdminReturn_AfterPartialCancellation_RestoresOnlyRemainingQuantity()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 2);
        var stockAfterCheckout = await GetStockAsync(WirelessMouse);
        var itemId = await GetOrderItemIdAsync(orderId);
        var etag = await GetOrderETagAsync(Alice, orderId);

        var cancelRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/orders/{orderId}/cancel")
        {
            Content = JsonContent.Create(new
            {
                items = new[] { new { orderItemId = itemId, quantity = 1 } }
            })
        };
        cancelRequest.Headers.Add("X-Test-User", Alice);
        cancelRequest.Headers.Add("If-Match", etag);
        var cancelResponse = await _client.SendAsync(cancelRequest);
        cancelResponse.EnsureSuccessStatusCode();

        var stockAfterPartialCancel = await GetStockAsync(WirelessMouse);
        Assert.Equal(stockAfterCheckout.Available + 1, stockAfterPartialCancel.Available);
        Assert.Equal(stockAfterCheckout.Reserved - 1, stockAfterPartialCancel.Reserved);

        await SetStatusAsync(orderId, OrderStatus.Delivered);

        var response = await SendAdminAsync(
            HttpMethod.Post,
            $"/api/admin/orders/{orderId}/return",
            Admin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderDetailResponse>();
        Assert.NotNull(order);
        Assert.Equal("Returned", order.Status);
        Assert.Equal(1, Assert.Single(order.Items).CancelledQuantity);

        var stockAfterReturn = await GetStockAsync(WirelessMouse);
        Assert.Equal(stockAfterCheckout.Available + 2, stockAfterReturn.Available);
        Assert.Equal(stockAfterPartialCancel.Reserved, stockAfterReturn.Reserved);
    }

    [Fact]
    public async Task AdminReturn_WhenOrderIsPaid_Returns409InvalidOrderTransition()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);
        var stockBefore = await GetStockAsync(WirelessMouse);
        await SetStatusAsync(orderId, OrderStatus.Paid);

        var response = await SendAdminAsync(
            HttpMethod.Post,
            $"/api/admin/orders/{orderId}/return",
            Admin);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("invalid_order_transition", error.Code);

        Assert.Equal(OrderStatus.Paid, await GetOrderStatusAsync(orderId));
        Assert.Equal(stockBefore, await GetStockAsync(WirelessMouse));
    }

    [Theory]
    [InlineData(Alice, "ship")]
    [InlineData(Alice, "deliver")]
    [InlineData(Alice, "return")]
    [InlineData(Bob, "ship")]
    [InlineData(Bob, "deliver")]
    [InlineData(Bob, "return")]
    public async Task NonAdmin_CannotTransitionOrders(string user, string action)
    {
        var response = await SendAdminAsync(
            HttpMethod.Post,
            $"/api/admin/orders/{Guid.NewGuid()}/{action}",
            user);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("forbidden", error.Code);
    }

    [Theory]
    [InlineData("ship")]
    [InlineData("deliver")]
    [InlineData("return")]
    public async Task AdminOrderEndpoints_AreNotFoundInProduction(string action)
    {
        using var factory = new CustomWebApplicationFactory(
            _fixture.ConnectionString,
            environment: "Production");
        using var client = factory.CreateClient();

        var response = await SendAdminAsync(
            HttpMethod.Post,
            $"/api/admin/orders/{Guid.NewGuid()}/{action}",
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

    private async Task<Guid> GetActiveProductIdAsync(string name = WirelessMouse)
    {
        await using var context = CreateContext();
        return await context.Products
            .Where(product => product.Name == name && product.IsActive)
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

    private async Task<Guid> CreateOrderAsync(string testUser, string productName, int quantity)
    {
        var productId = await GetActiveProductIdAsync(productName);

        var cartRequest = new HttpRequestMessage(HttpMethod.Get, "/api/cart");
        cartRequest.Headers.Add("X-Test-User", testUser);
        var cartResponse = await _client.SendAsync(cartRequest);
        cartResponse.EnsureSuccessStatusCode();
        var etag = cartResponse.Headers.ETag!.Tag;

        var addRequest = new HttpRequestMessage(HttpMethod.Post, "/api/cart/items")
        {
            Content = JsonContent.Create(new { productId, quantity })
        };
        addRequest.Headers.Add("X-Test-User", testUser);
        addRequest.Headers.Add("If-Match", etag);
        var addResponse = await _client.SendAsync(addRequest);
        addResponse.EnsureSuccessStatusCode();
        var newEtag = addResponse.Headers.ETag!.Tag;

        var orderRequest = new HttpRequestMessage(HttpMethod.Post, "/api/orders");
        orderRequest.Headers.Add("X-Test-User", testUser);
        orderRequest.Headers.Add("Idempotency-Key", $"admin-order-{Guid.NewGuid():N}".Substring(0, 32));
        orderRequest.Headers.Add("If-Match", newEtag);
        var orderResponse = await _client.SendAsync(orderRequest);
        orderResponse.EnsureSuccessStatusCode();

        var location = orderResponse.Headers.Location!.ToString();
        return Guid.Parse(location.Split('/').Last());
    }

    private async Task<Guid> GetOrderItemIdAsync(Guid orderId)
    {
        await using var context = CreateContext();
        return await context.OrderItems
            .Where(item => item.OrderId == orderId)
            .Select(item => item.Id)
            .FirstAsync();
    }

    private async Task<string> GetOrderETagAsync(string testUser, Guid orderId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{orderId}");
        request.Headers.Add("X-Test-User", testUser);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return response.Headers.ETag!.Tag;
    }

    private async Task<StockSnapshot> GetStockAsync(string productName)
    {
        await using var context = CreateContext();
        var stock = await context.Products
            .AsNoTracking()
            .Where(product => product.Name == productName)
            .Select(product => new { product.Available, product.Reserved })
            .FirstAsync();

        return new StockSnapshot(stock.Available, stock.Reserved);
    }

    private async Task<OrderStatus> GetOrderStatusAsync(Guid orderId)
    {
        await using var context = CreateContext();
        return await context.Orders
            .AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(order => order.Status)
            .FirstAsync();
    }

    private async Task<Guid?> GetPaymentIdAsync(Guid orderId)
    {
        await using var context = CreateContext();
        return await context.Orders
            .AsNoTracking()
            .Where(order => order.Id == orderId)
            .Select(order => order.PaymentId)
            .FirstAsync();
    }

    private async Task<Payment?> GetPaymentByOrderIdAsync(Guid orderId)
    {
        await using var context = CreateContext();
        return await context.Payments
            .AsNoTracking()
            .Include(payment => payment.Refunds)
            .FirstOrDefaultAsync(payment => payment.OrderId == orderId);
    }

    private async Task<Guid?> SetStatusAsync(Guid orderId, OrderStatus status)
    {
        await using var context = CreateContext();
        var storedPaymentId = await context.Orders
            .Where(order => order.Id == orderId)
            .Select(order => order.PaymentId)
            .FirstAsync();

        if (storedPaymentId is null)
        {
            var totalAmount = await context.Orders
                .Where(order => order.Id == orderId)
                .Select(order => order.TotalAmount)
                .FirstAsync();
            var payment = Payment.Create(orderId, totalAmount, DateTime.UtcNow);
            context.Payments.Add(payment);
            await context.SaveChangesAsync();
            storedPaymentId = payment.Id;
        }

        if (status is OrderStatus.Paid or OrderStatus.Shipped or OrderStatus.Delivered)
        {
            var payment = await context.Payments
                .FirstAsync(payment => payment.Id == storedPaymentId!.Value);

            if (payment.Status == PaymentStatus.Pending)
            {
                payment.MarkConfirmed(DateTime.UtcNow);
                await context.SaveChangesAsync();
            }
        }

        var affected = await context.Orders
            .Where(order => order.Id == orderId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(order => order.Status, status)
                    .SetProperty(order => order.PaymentId, storedPaymentId));

        Assert.Equal(1, affected);
        return storedPaymentId;
    }

    private async Task<AuditLog> GetAuditLogAsync(string action)
    {
        await using var context = CreateContext();
        return await context.AuditLogs
            .AsNoTracking()
            .SingleAsync(log => log.Action == action);
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options);

    private sealed record ErrorResponse(string Code);

    private sealed record ProductResponse(Guid Id, string Name, decimal Price, int Available);

    private sealed record OrderDetailResponse(Guid Id, string Status, DateTime CreatedAtUtc, decimal TotalAmount, List<OrderItemResponse> Items);

    private sealed record OrderItemResponse(Guid OrderItemId, Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, int CancelledQuantity, decimal LineTotal);

    private sealed record StockSnapshot(int Available, int Reserved);
}

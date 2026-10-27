using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Infrastructure;
using PS.MiniEcommerce.IntegrationTests.Fixtures;

namespace PS.MiniEcommerce.IntegrationTests;

[Collection("Integration")]
public class OrderCancelEndpointTests : IAsyncLifetime
{
    private const string Alice = "alice";
    private const string Bob = "bob";
    private const string WirelessMouse = "Wireless Mouse";
    private const string ValidIfMatch = "\"AQIDBAUGBwg=\"";

    private readonly TestDatabaseFixture _fixture;
    private readonly NotificationCapture _capture;
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public OrderCancelEndpointTests(TestDatabaseFixture fixture)
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

    [Fact]
    public async Task CancelOrder_WhenEligible_ReturnsCancelledOrderWithETag()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 2);
        var stockAfterCheckout = await GetStockAsync(WirelessMouse);

        var response = await CancelAsync(Alice, orderId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        var order = await response.Content.ReadFromJsonAsync<OrderDetailResponse>();
        Assert.NotNull(order);
        Assert.Equal(orderId, order.Id);
        Assert.Equal("Cancelled", order.Status);
        var item = Assert.Single(order.Items);
        Assert.Equal(2, item.CancelledQuantity);

        Assert.Equal(OrderStatus.Cancelled, await GetOrderStatusAsync(orderId));
        var stockAfterCancel = await GetStockAsync(WirelessMouse);
        Assert.Equal(stockAfterCheckout.Available + 2, stockAfterCancel.Available);
        Assert.Equal(0, stockAfterCancel.Reserved);

        var orderCancelled = Assert.Single(_capture.Events.OfType<OrderCancelled>());
        Assert.Equal(orderId, orderCancelled.OrderId);
        Assert.True(orderCancelled.IsFullCancellation);
        var cancelledItem = Assert.Single(orderCancelled.Items);
        Assert.Equal(2, cancelledItem.Quantity);
        Assert.True(_capture.OrderCancelledWasVisibleAfterCommit);
    }

    [Fact]
    public async Task CancelOrder_WhenAlreadyCancelled_Returns409WithOrderAlreadyCancelled()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 2);
        var first = await CancelAsync(Alice, orderId);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var stockAfterCancel = await GetStockAsync(WirelessMouse);

        var second = await CancelAsync(Alice, orderId);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var error = await second.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("order_already_cancelled", error.Code);

        Assert.Equal(stockAfterCancel, await GetStockAsync(WirelessMouse));
    }

    [Fact]
    public async Task CancelOrder_WhenOrderBelongsToAnotherUser_Returns404()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);
        var etag = await GetOrderETagAsync(Alice, orderId);

        var response = await SendCancelAsync(Bob, orderId, etag);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("order_not_found", error.Code);
    }

    [Fact]
    public async Task CancelOrder_WhenOrderDoesNotExist_Returns404()
    {
        var response = await SendCancelAsync(Alice, Guid.NewGuid(), ValidIfMatch);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("order_not_found", error.Code);
    }

    [Fact]
    public async Task CancelOrder_Unauthenticated_Returns401()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/cancel");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("unauthenticated", error.Code);
    }

    [Fact]
    public async Task CancelOrder_WithoutIfMatch_Returns428PreconditionRequired()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);

        var response = await SendCancelAsync(Alice, orderId, ifMatch: null);

        Assert.Equal(HttpStatusCode.PreconditionRequired, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("precondition_required", error.Code);
    }

    [Fact]
    public async Task CancelOrder_WithWildcardIfMatch_Returns400ValidationError()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);

        var response = await SendCancelAsync(Alice, orderId, "*");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("validation_error", error.Code);
    }

    [Fact]
    public async Task CancelOrder_WithInvalidIfMatch_Returns400ValidationError()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);

        var response = await SendCancelAsync(Alice, orderId, "\"not-base64\"");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("validation_error", error.Code);
    }

    [Fact]
    public async Task CancelOrder_WithStaleIfMatch_Returns412OrderVersionConflict()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);
        var staleEtag = await GetOrderETagAsync(Alice, orderId);
        await SetStatusAsync(orderId, OrderStatus.Created);
        var stockBeforeCancel = await GetStockAsync(WirelessMouse);

        var response = await SendCancelAsync(Alice, orderId, staleEtag);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("order_version_conflict", error.Code);

        Assert.Equal(OrderStatus.Created, await GetOrderStatusAsync(orderId));
        Assert.Equal(stockBeforeCancel, await GetStockAsync(WirelessMouse));
    }

    [Fact]
    public async Task CancelOrder_ConcurrentCancellationsWithSameETag_OneSucceedsOneConflicts()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 2);
        var etag = await GetOrderETagAsync(Alice, orderId);
        var stockAfterCheckout = await GetStockAsync(WirelessMouse);

        var tasks = new[]
        {
            Task.Run(() => SendCancelAsync(Alice, orderId, etag)),
            Task.Run(() => SendCancelAsync(Alice, orderId, etag))
        };

        var responses = await Task.WhenAll(tasks);
        var statuses = responses.Select(response => response.StatusCode).ToList();

        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.OK));
        var conflict = Assert.Single(
            responses,
            response => response.StatusCode == HttpStatusCode.PreconditionFailed);
        var error = await conflict.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("order_version_conflict", error.Code);

        Assert.Equal(OrderStatus.Cancelled, await GetOrderStatusAsync(orderId));
        var stockAfterCancel = await GetStockAsync(WirelessMouse);
        Assert.Equal(stockAfterCheckout.Available + 2, stockAfterCancel.Available);
        Assert.Equal(0, stockAfterCancel.Reserved);
    }

    [Fact]
    public async Task CancelOrder_ConcurrentPartialAndFullCancellationsWithSameETag_AreSerialized()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 2);
        var etag = await GetOrderETagAsync(Alice, orderId);
        var order = await GetOrderAsync(Alice, orderId);
        var item = Assert.Single(order.Items);
        var stockAfterCheckout = await GetStockAsync(WirelessMouse);

        var partialBody = new
        {
            items = new[]
            {
                new { orderItemId = item.OrderItemId, quantity = 1 }
            }
        };

        var responses = await Task.WhenAll(
            Task.Run(() => SendCancelAsync(Alice, orderId, etag, partialBody)),
            Task.Run(() => SendCancelAsync(Alice, orderId, etag)));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        var conflict = Assert.Single(
            responses,
            response => response.StatusCode == HttpStatusCode.PreconditionFailed);
        var error = await conflict.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("order_version_conflict", error.Code);

        var status = await GetOrderStatusAsync(orderId);
        var stockAfterCancel = await GetStockAsync(WirelessMouse);

        if (status == OrderStatus.Cancelled)
        {
            Assert.Equal(stockAfterCheckout.Available + 2, stockAfterCancel.Available);
            Assert.Equal(0, stockAfterCancel.Reserved);
            var cancelled = await GetOrderAsync(Alice, orderId);
            Assert.Equal(2, Assert.Single(cancelled.Items).CancelledQuantity);
        }
        else
        {
            Assert.Equal(OrderStatus.Created, status);
            Assert.Equal(stockAfterCheckout.Available + 1, stockAfterCancel.Available);
            Assert.Equal(1, stockAfterCancel.Reserved);
            var partiallyCancelled = await GetOrderAsync(Alice, orderId);
            Assert.Equal(1, Assert.Single(partiallyCancelled.Items).CancelledQuantity);
        }
    }

    [Fact]
    public async Task CancelOrder_WhenPaid_ReturnsCancelledAndRestoresAvailableButKeepsReserved()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 2);
        var stockAfterCheckout = await GetStockAsync(WirelessMouse);
        await SetStatusAsync(orderId, OrderStatus.Paid, Guid.NewGuid());

        var response = await CancelAsync(Alice, orderId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var order = await response.Content.ReadFromJsonAsync<OrderDetailResponse>();
        Assert.NotNull(order);
        Assert.Equal(orderId, order.Id);
        Assert.Equal("Cancelled", order.Status);

        Assert.Equal(OrderStatus.Cancelled, await GetOrderStatusAsync(orderId));
        var stockAfterCancel = await GetStockAsync(WirelessMouse);
        Assert.Equal(stockAfterCheckout.Available + 2, stockAfterCancel.Available);
        Assert.Equal(stockAfterCheckout.Reserved, stockAfterCancel.Reserved);

        var orderCancelled = Assert.Single(_capture.Events.OfType<OrderCancelled>());
        Assert.Equal(orderId, orderCancelled.OrderId);
        Assert.True(orderCancelled.IsFullCancellation);
        var cancelledItem = Assert.Single(orderCancelled.Items);
        Assert.Equal(2, cancelledItem.Quantity);
        Assert.True(_capture.OrderCancelledWasVisibleAfterCommit);
    }

    [Fact]
    public async Task CancelOrder_PartialCancellationOnPaid_RestoresAvailableOnly()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 2);
        var stockAfterCheckout = await GetStockAsync(WirelessMouse);
        await SetStatusAsync(orderId, OrderStatus.Paid, Guid.NewGuid());
        var etag = await GetOrderETagAsync(Alice, orderId);
        var order = await GetOrderAsync(Alice, orderId);
        var item = Assert.Single(order.Items);

        var response = await SendCancelAsync(
            Alice,
            orderId,
            etag,
            new
            {
                items = new[]
                {
                    new { orderItemId = item.OrderItemId, quantity = 1 }
                }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<OrderDetailResponse>();
        Assert.NotNull(updated);
        Assert.Equal("Paid", updated.Status);
        Assert.Equal(1, Assert.Single(updated.Items).CancelledQuantity);

        var stockAfterCancel = await GetStockAsync(WirelessMouse);
        Assert.Equal(stockAfterCheckout.Available + 1, stockAfterCancel.Available);
        Assert.Equal(stockAfterCheckout.Reserved, stockAfterCancel.Reserved);
    }

    [Fact]
    public async Task CancelOrder_PartialCancellation_UpdatesCancelledQuantityAndStockAndETag()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 2);
        var stockAfterCheckout = await GetStockAsync(WirelessMouse);
        var initialEtag = await GetOrderETagAsync(Alice, orderId);
        var initialOrder = await GetOrderAsync(Alice, orderId);
        var item = Assert.Single(initialOrder.Items);

        var partial = await SendCancelAsync(
            Alice,
            orderId,
            initialEtag,
            new
            {
                items = new[]
                {
                    new { orderItemId = item.OrderItemId, quantity = 1 }
                }
            });

        Assert.Equal(HttpStatusCode.OK, partial.StatusCode);
        Assert.NotNull(partial.Headers.ETag);
        Assert.NotEqual(initialEtag, partial.Headers.ETag!.Tag);
        var partialOrder = await partial.Content.ReadFromJsonAsync<OrderDetailResponse>();
        Assert.NotNull(partialOrder);
        Assert.Equal("Created", partialOrder.Status);
        var partialItem = Assert.Single(partialOrder.Items);
        Assert.Equal(1, partialItem.CancelledQuantity);

        var stockAfterPartial = await GetStockAsync(WirelessMouse);
        Assert.Equal(stockAfterCheckout.Available + 1, stockAfterPartial.Available);
        Assert.Equal(1, stockAfterPartial.Reserved);

        var afterPartial = await GetOrderByIdResponseAsync(Alice, orderId);
        Assert.Equal(partial.Headers.ETag, afterPartial.Headers.ETag);
        var refreshed = await afterPartial.Content.ReadFromJsonAsync<OrderDetailResponse>();
        Assert.NotNull(refreshed);
        Assert.Equal(1, Assert.Single(refreshed.Items).CancelledQuantity);

        var final = await SendCancelAsync(Alice, orderId, partial.Headers.ETag!.Tag);

        Assert.Equal(HttpStatusCode.OK, final.StatusCode);
        var finalOrder = await final.Content.ReadFromJsonAsync<OrderDetailResponse>();
        Assert.NotNull(finalOrder);
        Assert.Equal("Cancelled", finalOrder.Status);
        Assert.Equal(2, Assert.Single(finalOrder.Items).CancelledQuantity);

        var stockAfterCancel = await GetStockAsync(WirelessMouse);
        Assert.Equal(stockAfterCheckout.Available + 2, stockAfterCancel.Available);
        Assert.Equal(0, stockAfterCancel.Reserved);
    }

    [Fact]
    public async Task CancelOrder_PartialCancellationExceedingRemaining_Returns400AndLeavesStock()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 2);
        var stockBeforeCancel = await GetStockAsync(WirelessMouse);
        var etag = await GetOrderETagAsync(Alice, orderId);
        var order = await GetOrderAsync(Alice, orderId);
        var item = Assert.Single(order.Items);

        var response = await SendCancelAsync(
            Alice,
            orderId,
            etag,
            new
            {
                items = new[]
                {
                    new { orderItemId = item.OrderItemId, quantity = 3 }
                }
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("validation_error", error.Code);

        Assert.Equal(OrderStatus.Created, await GetOrderStatusAsync(orderId));
        Assert.Equal(stockBeforeCancel, await GetStockAsync(WirelessMouse));
    }

    [Fact]
    public async Task CancelOrder_WithEmptyItems_Returns400ValidationError()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);
        var etag = await GetOrderETagAsync(Alice, orderId);

        var response = await SendCancelAsync(
            Alice,
            orderId,
            etag,
            new { items = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("validation_error", error.Code);
    }

    [Fact]
    public async Task CancelOrder_WithUnknownOrderItem_Returns400ValidationError()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);
        var etag = await GetOrderETagAsync(Alice, orderId);

        var response = await SendCancelAsync(
            Alice,
            orderId,
            etag,
            new
            {
                items = new[]
                {
                    new { orderItemId = Guid.NewGuid(), quantity = 1 }
                }
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("validation_error", error.Code);
    }

    [Fact]
    public async Task CancelOrder_WithUnsupportedContentType_Returns415UnsupportedMediaType()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);
        var etag = await GetOrderETagAsync(Alice, orderId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/cancel")
        {
            Content = new StringContent("{\"items\":[]}", Encoding.UTF8, "text/plain")
        };
        request.Headers.Add("X-Test-User", Alice);
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("unsupported_media_type", error.Code);
    }

    [Theory]
    [InlineData(OrderStatus.Shipped)]
    [InlineData(OrderStatus.Delivered)]
    [InlineData(OrderStatus.Returned)]
    public async Task CancelOrder_WhenStatusDoesNotAllowCancellation_Returns409InvalidTransition(
        OrderStatus status)
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);
        var stockBeforeCancel = await GetStockAsync(WirelessMouse);
        await SetStatusAsync(orderId, status);

        var response = await CancelAsync(Alice, orderId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.NotNull(error);
        Assert.Equal("invalid_order_transition", error.Code);

        Assert.Equal(status, await GetOrderStatusAsync(orderId));
        Assert.Equal(stockBeforeCancel, await GetStockAsync(WirelessMouse));
    }

    [Fact]
    public async Task PaymentId_IsNullableAndPersists()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);

        await using (var context = CreateContext())
        {
            var initialPaymentId = await context.Orders
                .AsNoTracking()
                .Where(order => order.Id == orderId)
                .Select(order => order.PaymentId)
                .FirstAsync();

            Assert.Null(initialPaymentId);

            var paymentId = Guid.NewGuid();
            var affected = await context.Orders
                .Where(order => order.Id == orderId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(order => order.PaymentId, paymentId));

            Assert.Equal(1, affected);
        }

        await using (var context = CreateContext())
        {
            var storedPaymentId = await context.Orders
                .AsNoTracking()
                .Where(order => order.Id == orderId)
                .Select(order => order.PaymentId)
                .FirstAsync();

            Assert.NotNull(storedPaymentId);
            Assert.NotEqual(Guid.Empty, storedPaymentId.Value);
        }
    }

    [Fact]
    public async Task StatusCheck_AcceptsSixStatusesAndRejectsUnknown()
    {
        var orderId = await CreateOrderAsync(Alice, WirelessMouse, 1);

        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            await SetStatusAsync(orderId, status);

            Assert.Equal(status, await GetOrderStatusAsync(orderId));
        }

        await using (var context = CreateContext())
        {
            var exception = await Record.ExceptionAsync(() =>
                context.Database.ExecuteSqlRawAsync(
                    "UPDATE Orders SET Status = {0} WHERE Id = {1}",
                    "Unknown",
                    orderId));

            Assert.NotNull(exception);
            Assert.Contains("CK_Orders_Status_Valid", exception.Message);
        }

        Assert.Equal(OrderStatus.Returned, await GetOrderStatusAsync(orderId));
    }

    private async Task<HttpResponseMessage> CancelAsync(string testUser, Guid orderId)
    {
        var etag = await GetOrderETagAsync(testUser, orderId);
        return await SendCancelAsync(testUser, orderId, etag);
    }

    private Task<HttpResponseMessage> SendCancelAsync(
        string testUser,
        Guid orderId,
        string? ifMatch,
        object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/orders/{orderId}/cancel");
        request.Headers.Add("X-Test-User", testUser);

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return _client.SendAsync(request);
    }

    private async Task<string> GetOrderETagAsync(string testUser, Guid orderId)
    {
        var response = await GetOrderByIdResponseAsync(testUser, orderId);
        response.EnsureSuccessStatusCode();
        return response.Headers.ETag!.Tag;
    }

    private async Task<OrderDetailResponse> GetOrderAsync(string testUser, Guid orderId)
    {
        var response = await GetOrderByIdResponseAsync(testUser, orderId);
        response.EnsureSuccessStatusCode();
        var order = await response.Content.ReadFromJsonAsync<OrderDetailResponse>();
        Assert.NotNull(order);
        return order;
    }

    private Task<HttpResponseMessage> GetOrderByIdResponseAsync(string testUser, Guid orderId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/orders/{orderId}");
        request.Headers.Add("X-Test-User", testUser);
        return _client.SendAsync(request);
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
        orderRequest.Headers.Add("Idempotency-Key", $"test-order-{Guid.NewGuid():N}".Substring(0, 32));
        orderRequest.Headers.Add("If-Match", newEtag);
        var orderResponse = await _client.SendAsync(orderRequest);
        orderResponse.EnsureSuccessStatusCode();

        var location = orderResponse.Headers.Location!.ToString();
        return Guid.Parse(location.Split('/').Last());
    }

    private async Task<Guid> GetActiveProductIdAsync(string name)
    {
        await using var context = CreateContext();
        return await context.Products
            .Where(product => product.Name == name && product.IsActive)
            .Select(product => product.Id)
            .FirstAsync();
    }

    private async Task<StockSnapshot> GetStockAsync(string name)
    {
        await using var context = CreateContext();
        var stock = await context.Products
            .AsNoTracking()
            .Where(product => product.Name == name)
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

    private async Task SetStatusAsync(Guid orderId, OrderStatus status, Guid? paymentId = null)
    {
        await using var context = CreateContext();
        var affected = await context.Orders
            .Where(order => order.Id == orderId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(order => order.Status, status)
                    .SetProperty(order => order.PaymentId, paymentId));

        Assert.Equal(1, affected);
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .Options);

    private sealed record OrderDetailResponse(Guid Id, string Status, DateTime CreatedAtUtc, decimal TotalAmount, List<OrderItemResponse> Items);

    private sealed record OrderItemResponse(Guid OrderItemId, Guid ProductId, string ProductName, decimal UnitPrice, int Quantity, int CancelledQuantity, decimal LineTotal);

    private sealed record ErrorResponse(string Code);

    private sealed record StockSnapshot(int Available, int Reserved);
}

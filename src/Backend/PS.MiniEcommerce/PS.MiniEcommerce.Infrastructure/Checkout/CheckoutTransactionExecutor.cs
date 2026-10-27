using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PS.MiniEcommerce.Application.Carts;
using PS.MiniEcommerce.Application.Checkout;
using PS.MiniEcommerce.Application.Checkout.Exceptions;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Application.Orders.DTOs;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Exceptions;
using PS.MiniEcommerce.Infrastructure.Persistence;

namespace PS.MiniEcommerce.Infrastructure.Checkout;

public class CheckoutTransactionExecutor : ICheckoutTransactionExecutor
{
    private const int CreatedStatusCode = 201;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _dbContext;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<CheckoutTransactionExecutor> _logger;

    public CheckoutTransactionExecutor(
        AppDbContext dbContext,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<CheckoutTransactionExecutor> logger)
    {
        _dbContext = dbContext;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    public async Task<CheckoutExecutionResult> ExecuteAsync(
        Guid userId,
        Guid checkoutAttemptId,
        string? ifMatch,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteSuccessPathAsync(
                userId,
                checkoutAttemptId,
                ifMatch,
                utcNow,
                cancellationToken);
        }
        catch (DomainConflictException exception) when (exception.Code == "checkout_retry")
        {
            return await HandleCheckoutRetryAsync(
                checkoutAttemptId,
                utcNow,
                exception,
                cancellationToken);
        }
        catch (DomainException exception)
        {
            return await PersistErrorAsync(checkoutAttemptId, exception, utcNow, cancellationToken);
        }
        catch (NotFoundException exception)
        {
            return await PersistErrorAsync(checkoutAttemptId, exception, utcNow, cancellationToken);
        }
        catch (ValidationException exception)
        {
            return await PersistErrorAsync(checkoutAttemptId, exception, utcNow, cancellationToken);
        }
    }

    private async Task<CheckoutExecutionResult> ExecuteSuccessPathAsync(
        Guid userId,
        Guid checkoutAttemptId,
        string? ifMatch,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var cart = await _dbContext.Carts
            .FromSqlInterpolated($"SELECT * FROM Carts WITH (UPDLOCK) WHERE UserId = {userId}")
            .Include(c => c.Items.Where(item => item.IsSelected))
            .FirstOrDefaultAsync(cancellationToken);

        if (cart is null)
        {
            throw new NotFoundException("Cart not found.", "cart_not_found");
        }

        var rowVersion = _dbContext.Entry(cart).Property<byte[]>("RowVersion").CurrentValue;
        CartVersionGuard.Validate(ifMatch, rowVersion);

        if (cart.Items.Count == 0)
        {
            throw new DomainConflictException("no selected items", "cart_empty");
        }

        var productIds = cart.Items
            .Select(item => item.ProductId)
            .OrderBy(id => id)
            .ToList();

        var products = await _dbContext.Products
            .AsNoTracking()
            .Where(product => productIds.Contains(product.Id))
            .Select(product => new
            {
                product.Id,
                product.Name,
                product.IsActive,
                product.Available
            })
            .ToListAsync(cancellationToken);

        var productsById = products.ToDictionary(product => product.Id);
        var requestedQuantities = cart.Items.ToDictionary(
            item => item.ProductId,
            item => item.Quantity);

        var hasPreliminaryStockOrAvailabilityProblem = cart.Items.Any(item =>
        {
            var product = productsById[item.ProductId];
            return !product.IsActive || item.Quantity > product.Available;
        });

        if (hasPreliminaryStockOrAvailabilityProblem)
        {
            var error = await BuildStockOrAvailabilityErrorAsync(
                requestedQuantities,
                cancellationToken);

            if (error is not null)
            {
                throw error;
            }
        }

        foreach (var productId in productIds)
        {
            var item = cart.Items.First(cartItem => cartItem.ProductId == productId);

            var affected = await _dbContext.Products
                .Where(product => product.Id == productId && product.Available >= item.Quantity)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            product => product.Available,
                            product => product.Available - item.Quantity)
                        .SetProperty(
                            product => product.Reserved,
                            product => product.Reserved + item.Quantity),
                    cancellationToken);

            if (affected == 0)
            {
                await transaction.RollbackAsync(cancellationToken);

                var error = await BuildStockOrAvailabilityErrorAsync(
                    requestedQuantities,
                    cancellationToken);

                if (error is null)
                {
                    throw new DomainConflictException(
                        "The checkout state changed concurrently after rollback. Retry the same request with the same idempotency key.",
                        "checkout_retry");
                }

                throw error;
            }
        }

        var productSnapshots = await _dbContext.Products
            .AsNoTracking()
            .Where(product => productIds.Contains(product.Id))
            .OrderBy(product => product.Id)
            .Select(product => new
            {
                product.Id,
                product.Name,
                product.Price
            })
            .ToListAsync(cancellationToken);

        var productSnapshotsById = productSnapshots.ToDictionary(product => product.Id);

        var orderItems = cart.Items
            .Select(item =>
            {
                var product = productSnapshotsById[item.ProductId];
                return new OrderItemData(item.ProductId, product.Name, product.Price, item.Quantity);
            })
            .ToList();

        var order = Order.Create(userId, orderItems, utcNow);
        _dbContext.Orders.Add(order);

        cart.RemoveSelected(utcNow);

        var orderDto = order.ToDto();
        var responsePayload = JsonSerializer.Serialize(orderDto, JsonOptions);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);

            var markedCompleted = await _dbContext.CheckoutAttempts
                .Where(checkoutAttempt =>
                    checkoutAttempt.Id == checkoutAttemptId
                    && checkoutAttempt.Status == CheckoutAttemptStatus.InProgress)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(checkoutAttempt => checkoutAttempt.Status, CheckoutAttemptStatus.Completed)
                        .SetProperty(checkoutAttempt => checkoutAttempt.OrderId, order.Id)
                        .SetProperty(checkoutAttempt => checkoutAttempt.ResponseStatusCode, CreatedStatusCode)
                        .SetProperty(checkoutAttempt => checkoutAttempt.ResponsePayload, responsePayload)
                        .SetProperty(checkoutAttempt => checkoutAttempt.UpdatedAtUtc, utcNow),
                    cancellationToken);

            if (markedCompleted == 0)
            {
                await transaction.RollbackAsync(cancellationToken);

                var existing = await _dbContext.CheckoutAttempts
                    .AsNoTracking()
                    .FirstAsync(checkoutAttempt => checkoutAttempt.Id == checkoutAttemptId, cancellationToken);

                byte[]? existingOrderRowVersion = null;
                if (existing.OrderId is not null)
                {
                    existingOrderRowVersion = await _dbContext.Orders
                        .AsNoTracking()
                        .Where(existingOrder => existingOrder.Id == existing.OrderId)
                        .Select(existingOrder => EF.Property<byte[]>(existingOrder, "RowVersion"))
                        .FirstOrDefaultAsync(cancellationToken);
                }

                return new CheckoutExecutionResult(
                    Success: existing.ResponseStatusCode == CreatedStatusCode,
                    ResponseStatusCode: existing.ResponseStatusCode!.Value,
                    ResponsePayload: existing.ResponsePayload!,
                    OrderId: existing.OrderId,
                    NewOrderRowVersion: existingOrderRowVersion,
                    DomainEvents: Array.Empty<object>());
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not DomainException)
        {
            throw new CheckoutCommitUncertainException(
                "Checkout commit outcome is uncertain. Retry with the same idempotency key.",
                exception);
        }

        var newRowVersion = await _dbContext.Orders
            .AsNoTracking()
            .Where(createdOrder => createdOrder.Id == order.Id)
            .Select(createdOrder => EF.Property<byte[]>(createdOrder, "RowVersion"))
            .FirstAsync(cancellationToken);

        var domainEvents = order.DomainEvents.ToList();
        order.ClearDomainEvents();

        return new CheckoutExecutionResult(
            Success: true,
            ResponseStatusCode: CreatedStatusCode,
            ResponsePayload: responsePayload,
            OrderId: order.Id,
            NewOrderRowVersion: newRowVersion,
            DomainEvents: domainEvents);
    }

    private async Task<DomainConflictException?> BuildStockOrAvailabilityErrorAsync(
        IReadOnlyDictionary<Guid, int> requestedQuantities,
        CancellationToken cancellationToken)
    {
        // Always read in a fresh scope/context so the result reflects committed state,
        // regardless of the caller's active or already rolled-back transaction.
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var productIds = requestedQuantities.Keys
            .OrderBy(id => id)
            .ToList();

        var products = await dbContext.Products
            .AsNoTracking()
            .Where(product => productIds.Contains(product.Id))
            .Select(product => new
            {
                product.Id,
                product.Name,
                product.IsActive,
                product.Available
            })
            .ToListAsync(cancellationToken);

        var productsById = products.ToDictionary(product => product.Id);
        var unavailable = new List<AffectedProduct>();
        var insufficient = new List<AffectedProduct>();

        foreach (var productId in productIds)
        {
            if (!productsById.TryGetValue(productId, out var product))
            {
                continue;
            }

            if (!product.IsActive)
            {
                unavailable.Add(new AffectedProduct(product.Id, product.Name));
            }
            else if (product.Available < requestedQuantities[productId])
            {
                insufficient.Add(new AffectedProduct(product.Id, product.Name));
            }
        }

        if (unavailable.Count > 0)
        {
            return new DomainConflictException(
                CheckoutStockErrorDetails.FormatUnavailableProducts(unavailable),
                "product_unavailable");
        }

        if (insufficient.Count > 0)
        {
            return new DomainConflictException(
                CheckoutStockErrorDetails.FormatInsufficientStock(insufficient),
                "insufficient_stock");
        }

        return null;
    }

    private async Task<CheckoutExecutionResult> HandleCheckoutRetryAsync(
        Guid checkoutAttemptId,
        DateTime utcNow,
        DomainConflictException exception,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Checkout attempt {CheckoutAttemptId} could not reproduce a stock or availability conflict after rollback; leaving it in progress for retry.",
            checkoutAttemptId);

        var expiredLease = utcNow.AddSeconds(-1);

        await _dbContext.CheckoutAttempts
            .Where(attempt =>
                attempt.Id == checkoutAttemptId
                && attempt.Status == CheckoutAttemptStatus.InProgress)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(attempt => attempt.LeaseExpiresAtUtc, expiredLease)
                    .SetProperty(attempt => attempt.UpdatedAtUtc, utcNow),
                cancellationToken);

        var (statusCode, responsePayload) = MapToProblemDetails(exception);

        return new CheckoutExecutionResult(
            Success: false,
            ResponseStatusCode: statusCode,
            ResponsePayload: responsePayload,
            OrderId: null,
            NewOrderRowVersion: null,
            DomainEvents: Array.Empty<object>());
    }

    private async Task<CheckoutExecutionResult> PersistErrorAsync(
        Guid checkoutAttemptId,
        Exception exception,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var (statusCode, responsePayload) = MapToProblemDetails(exception);

        _logger.LogInformation(
            "Checkout attempt {CheckoutAttemptId} ended with final business error status {StatusCode}.",
            checkoutAttemptId,
            statusCode);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var attempt = await _dbContext.CheckoutAttempts
            .FirstAsync(checkoutAttempt => checkoutAttempt.Id == checkoutAttemptId, cancellationToken);

        attempt.MarkCompletedWithError(statusCode, responsePayload, utcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CheckoutExecutionResult(
            Success: false,
            ResponseStatusCode: statusCode,
            ResponsePayload: responsePayload,
            OrderId: null,
            NewOrderRowVersion: null,
            DomainEvents: Array.Empty<object>());
    }

    private static (int StatusCode, string Payload) MapToProblemDetails(Exception exception)
    {
        return exception switch
        {
            ValidationException validation => (400, BuildPayload(
                "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                "One or more validation errors occurred.",
                400,
                "The request body is invalid.",
                "validation_error",
                validation.Errors)),

            DomainValidationException domainValidation => (400, BuildPayload(
                "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                "Validation error.",
                400,
                domainValidation.Message,
                "validation_error")),

            DomainConflictException domainConflict => MapConflict(domainConflict),

            NotFoundException notFound => (404, BuildPayload(
                "https://tools.ietf.org/html/rfc9110#section-15.5.5",
                "Resource not found.",
                404,
                notFound.Message,
                notFound.Code)),

            _ => (409, BuildPayload(
                "https://tools.ietf.org/html/rfc9110#section-15.5.10",
                "Conflict.",
                409,
                exception.Message,
                "conflict"))
        };
    }

    private static (int StatusCode, string Payload) MapConflict(DomainConflictException exception)
    {
        return exception.Code switch
        {
            "precondition_required" => (428, BuildPayload(
                "https://tools.ietf.org/html/rfc6585#section-3",
                "Precondition required.",
                428,
                exception.Message,
                exception.Code)),

            "cart_version_conflict" => (412, BuildPayload(
                "https://tools.ietf.org/html/rfc9110#section-15.5.13",
                "Precondition failed.",
                412,
                exception.Message,
                exception.Code)),

            "idempotency_in_progress" => (503, BuildPayload(
                "https://tools.ietf.org/html/rfc9110#section-15.6.4",
                "Service unavailable.",
                503,
                exception.Message,
                exception.Code)),

            "checkout_retry" => (503, BuildPayload(
                "https://tools.ietf.org/html/rfc9110#section-15.6.4",
                "Service unavailable.",
                503,
                exception.Message,
                exception.Code)),

            _ => (409, BuildPayload(
                "https://tools.ietf.org/html/rfc9110#section-15.5.10",
                "Conflict.",
                409,
                exception.Message,
                exception.Code))
        };
    }

    private static string BuildPayload(
        string type,
        string title,
        int status,
        string detail,
        string code,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["title"] = title,
            ["status"] = status,
            ["detail"] = detail,
            ["code"] = code
        };

        if (errors is not null)
        {
            payload["errors"] = errors;
        }

        return JsonSerializer.Serialize(payload, JsonOptions);
    }
}

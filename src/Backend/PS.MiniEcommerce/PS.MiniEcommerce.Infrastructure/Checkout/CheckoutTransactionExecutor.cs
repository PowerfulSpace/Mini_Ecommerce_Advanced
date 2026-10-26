using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
    private readonly ILogger<CheckoutTransactionExecutor> _logger;

    public CheckoutTransactionExecutor(
        AppDbContext dbContext,
        ILogger<CheckoutTransactionExecutor> logger)
    {
        _dbContext = dbContext;
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
            .Include(c => c.Items)
            .FirstOrDefaultAsync(cancellationToken);

        if (cart is null)
        {
            throw new NotFoundException("Cart not found.", "cart_not_found");
        }

        var rowVersion = _dbContext.Entry(cart).Property<byte[]>("RowVersion").CurrentValue;
        CartVersionGuard.Validate(ifMatch, rowVersion);

        if (cart.Items.Count == 0)
        {
            throw new DomainConflictException("Cart is empty.", "cart_empty");
        }

        var productIds = cart.Items
            .Select(item => item.ProductId)
            .OrderBy(id => id)
            .ToList();

        var products = await _dbContext.Products
            .Where(product => productIds.Contains(product.Id))
            .ToListAsync(cancellationToken);

        var productsById = products.ToDictionary(product => product.Id);

        foreach (var item in cart.Items)
        {
            var product = productsById[item.ProductId];

            if (!product.IsActive)
            {
                throw new DomainConflictException(
                    $"Product '{product.Name}' is not available.",
                    "product_unavailable");
            }

            if (item.Quantity > product.StockQuantity)
            {
                throw new DomainConflictException(
                    $"Insufficient stock for product '{product.Name}'.",
                    "insufficient_stock");
            }
        }

        foreach (var productId in productIds)
        {
            var item = cart.Items.First(cartItem => cartItem.ProductId == productId);

            var affected = await _dbContext.Products
                .Where(product => product.Id == productId && product.StockQuantity >= item.Quantity)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        product => product.StockQuantity,
                        product => product.StockQuantity - item.Quantity),
                    cancellationToken);

            if (affected == 0)
            {
                throw new DomainConflictException(
                    $"Insufficient stock for product '{productsById[productId].Name}'.",
                    "insufficient_stock");
            }
        }

        var orderItems = cart.Items
            .Select(item =>
            {
                var product = productsById[item.ProductId];
                return new OrderItemData(item.ProductId, product.Name, product.Price, item.Quantity);
            })
            .ToList();

        var order = Order.Create(userId, orderItems, utcNow);
        _dbContext.Orders.Add(order);

        cart.Clear(utcNow);

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

                return new CheckoutExecutionResult(
                    Success: existing.ResponseStatusCode == CreatedStatusCode,
                    ResponseStatusCode: existing.ResponseStatusCode!.Value,
                    ResponsePayload: existing.ResponsePayload!,
                    OrderId: existing.OrderId,
                    NewCartRowVersion: null);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not DomainException)
        {
            throw new CheckoutCommitUncertainException(
                "Checkout commit outcome is uncertain. Retry with the same idempotency key.",
                exception);
        }

        var newRowVersion = await _dbContext.Carts
            .AsNoTracking()
            .Where(c => c.Id == cart.Id)
            .Select(c => EF.Property<byte[]>(c, "RowVersion"))
            .FirstAsync(cancellationToken);

        return new CheckoutExecutionResult(
            Success: true,
            ResponseStatusCode: CreatedStatusCode,
            ResponsePayload: responsePayload,
            OrderId: order.Id,
            NewCartRowVersion: newRowVersion);
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
            NewCartRowVersion: null);
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

using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Events;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Application.Payments.Commands;

public class ConfirmPaymentHandler : IRequestHandler<ConfirmPaymentCommand, ConfirmExecutionResult>
{
    private const string PaymentFailedCode = "payment_failed";

    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan BoundedWaitTimeout = TimeSpan.FromSeconds(3);

    private readonly IPaymentAttemptRepository _paymentAttemptRepository;
    private readonly IConfirmTransactionExecutor _confirmTransactionExecutor;
    private readonly ICurrentUser _currentUser;
    private readonly IMediator _mediator;
    private readonly ILogger<ConfirmPaymentHandler> _logger;
    private readonly ConfirmAttemptContext? _confirmAttemptContext;

    public ConfirmPaymentHandler(
        IPaymentAttemptRepository paymentAttemptRepository,
        IConfirmTransactionExecutor confirmTransactionExecutor,
        ICurrentUser currentUser,
        IMediator mediator,
        ILogger<ConfirmPaymentHandler> logger,
        ConfirmAttemptContext? confirmAttemptContext = null)
    {
        _paymentAttemptRepository = paymentAttemptRepository;
        _confirmTransactionExecutor = confirmTransactionExecutor;
        _currentUser = currentUser;
        _mediator = mediator;
        _logger = logger;
        _confirmAttemptContext = confirmAttemptContext;
    }

    public async Task<ConfirmExecutionResult> Handle(
        ConfirmPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        var utcNow = DateTime.UtcNow;

        var existing = await _paymentAttemptRepository.GetByUserAndKeyAsync(
            userId,
            request.IdempotencyKey,
            cancellationToken);

        if (existing is not null)
        {
            return await PublishAndReturnAsync(
                await HandleExistingAttemptAsync(existing, request, userId, cancellationToken),
                cancellationToken);
        }

        var reserved = await _paymentAttemptRepository.ReserveAsync(
            userId,
            request.IdempotencyKey,
            request.RequestFingerprint,
            utcNow,
            LeaseDuration,
            cancellationToken);

        if (reserved is null)
        {
            var raced = await _paymentAttemptRepository.GetByUserAndKeyAsync(
                userId,
                request.IdempotencyKey,
                cancellationToken);

            if (raced is null)
            {
                throw new InvalidOperationException(
                    "Reservation failed but no existing payment attempt was found.");
            }

            return await PublishAndReturnAsync(
                await HandleExistingAttemptAsync(raced, request, userId, cancellationToken),
                cancellationToken);
        }

        return await PublishAndReturnAsync(
            await ExecuteWithErrorPersistenceAsync(
                reserved.Id,
                request,
                userId,
                isRetry: false,
                cancellationToken),
            cancellationToken);
    }

    private async Task<ConfirmExecutionResult> HandleExistingAttemptAsync(
        PaymentAttemptData attempt,
        ConfirmPaymentCommand request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!attempt.RequestFingerprint.SequenceEqual(request.RequestFingerprint))
        {
            throw new DomainConflictException(
                "Idempotency key was reused with a different request.",
                "idempotency_key_reused");
        }

        if (attempt.State == PaymentAttemptState.InProgress)
        {
            var completed = await _paymentAttemptRepository.WaitForCompletionAsync(
                userId,
                request.IdempotencyKey,
                BoundedWaitTimeout,
                cancellationToken);

            if (completed is not null)
            {
                return BuildReplayResult(completed);
            }

            var utcNow = DateTime.UtcNow;

            if (attempt.LeaseExpiresAtUtc > utcNow)
            {
                throw new DomainConflictException(
                    "A payment attempt with this idempotency key is already in progress.",
                    "idempotency_in_progress");
            }

            var claimed = await _paymentAttemptRepository.TryClaimExpiredAsync(
                attempt.Id,
                utcNow,
                LeaseDuration,
                cancellationToken);

            if (!claimed)
            {
                throw new DomainConflictException(
                    "A payment attempt with this idempotency key is already in progress.",
                    "idempotency_in_progress");
            }

            var durableState = await _paymentAttemptRepository.GetByUserAndKeyAsync(
                userId,
                request.IdempotencyKey,
                cancellationToken);

            if (durableState is null)
            {
                throw new InvalidOperationException(
                    "Payment attempt disappeared after successful claim.");
            }

            if (durableState.State == PaymentAttemptState.Completed)
            {
                return BuildReplayResult(durableState);
            }

            return await ExecuteWithErrorPersistenceAsync(
                durableState.Id,
                request,
                userId,
                isRetry: true,
                cancellationToken);
        }

        if (IsPaymentFailed(attempt))
        {
            var reopened = await _paymentAttemptRepository.TryReopenFailedAsync(
                attempt.Id,
                DateTime.UtcNow,
                LeaseDuration,
                cancellationToken);

            if (reopened)
            {
                return await ExecuteWithErrorPersistenceAsync(
                    attempt.Id,
                    request,
                    userId,
                    isRetry: true,
                    cancellationToken);
            }

            var completed = await _paymentAttemptRepository.WaitForCompletionAsync(
                userId,
                request.IdempotencyKey,
                BoundedWaitTimeout,
                cancellationToken);

            if (completed is not null)
            {
                return BuildReplayResult(completed);
            }

            throw new DomainConflictException(
                "A payment attempt with this idempotency key is already in progress.",
                "idempotency_in_progress");
        }

        return BuildReplayResult(attempt);
    }

    private async Task<ConfirmExecutionResult> ExecuteWithErrorPersistenceAsync(
        Guid attemptId,
        ConfirmPaymentCommand request,
        Guid userId,
        bool isRetry,
        CancellationToken cancellationToken)
    {
        try
        {
            if (_confirmAttemptContext is not null)
            {
                _confirmAttemptContext.PaymentAttemptId = attemptId;
                _confirmAttemptContext.IsRetry = isRetry;
            }

            return await _confirmTransactionExecutor.ExecuteAsync(
                request.PaymentId,
                userId,
                cancellationToken);
        }
        catch (Exception exception) when (IsBusinessError(exception))
        {
            var (statusCode, responsePayload) = PaymentProblemDetails.Map(exception);

            await _paymentAttemptRepository.CompleteWithErrorAsync(
                attemptId,
                statusCode,
                responsePayload,
                DateTime.UtcNow,
                cancellationToken);

            throw;
        }
        finally
        {
            if (_confirmAttemptContext is not null)
            {
                _confirmAttemptContext.PaymentAttemptId = null;
                _confirmAttemptContext.IsRetry = false;
            }
        }
    }

    private static ConfirmExecutionResult BuildReplayResult(PaymentAttemptData attempt)
    {
        if (attempt.ResponseStatusCode is null || attempt.ResponsePayload is null)
        {
            throw new InvalidOperationException(
                "Completed payment attempt does not contain a response.");
        }

        return new ConfirmExecutionResult(
            Success: attempt.ResponseStatusCode == 200,
            ResponseStatusCode: attempt.ResponseStatusCode.Value,
            ResponsePayload: attempt.ResponsePayload,
            OrderId: TryGetOrderId(attempt.ResponsePayload) ?? Guid.Empty,
            PaymentId: attempt.PaymentId ?? Guid.Empty,
            DomainEvents: Array.Empty<object>());
    }

    private async Task<ConfirmExecutionResult> PublishAndReturnAsync(
        ConfirmExecutionResult result,
        CancellationToken cancellationToken)
    {
        if (!result.Success)
        {
            return result;
        }

        foreach (var domainEvent in result.DomainEvents)
        {
            try
            {
                await _mediator.Publish(
                    new DomainEventNotification<object>(domainEvent),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to publish domain event {DomainEventType}.",
                    domainEvent.GetType().Name);
            }
        }

        return result;
    }

    private static bool IsPaymentFailed(PaymentAttemptData attempt)
    {
        return attempt.ResponseStatusCode == 409
            && attempt.ResponsePayload?.Contains(PaymentFailedCode, StringComparison.Ordinal) == true;
    }

    private static Guid? TryGetOrderId(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);

            if (document.RootElement.TryGetProperty("orderId", out var orderId)
                && orderId.TryGetGuid(out var value))
            {
                return value;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static bool IsBusinessError(Exception exception)
    {
        return exception is DomainException
            or NotFoundException
            or ValidationException;
    }
}

using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Events;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Application.Payments.Commands;

public class RefundPaymentHandler : IRequestHandler<RefundPaymentCommand, RefundExecutionResult>
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan BoundedWaitTimeout = TimeSpan.FromSeconds(3);

    private readonly IPaymentAttemptRepository _paymentAttemptRepository;
    private readonly IRefundTransactionExecutor _refundTransactionExecutor;
    private readonly ICurrentUser _currentUser;
    private readonly IMediator _mediator;
    private readonly ILogger<RefundPaymentHandler> _logger;

    public RefundPaymentHandler(
        IPaymentAttemptRepository paymentAttemptRepository,
        IRefundTransactionExecutor refundTransactionExecutor,
        ICurrentUser currentUser,
        IMediator mediator,
        ILogger<RefundPaymentHandler> logger)
    {
        _paymentAttemptRepository = paymentAttemptRepository;
        _refundTransactionExecutor = refundTransactionExecutor;
        _currentUser = currentUser;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<RefundExecutionResult> Handle(
        RefundPaymentCommand request,
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
                cancellationToken),
            cancellationToken);
    }

    private async Task<RefundExecutionResult> HandleExistingAttemptAsync(
        PaymentAttemptData attempt,
        RefundPaymentCommand request,
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
                cancellationToken);
        }

        return BuildReplayResult(attempt);
    }

    private async Task<RefundExecutionResult> ExecuteWithErrorPersistenceAsync(
        Guid attemptId,
        RefundPaymentCommand request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _refundTransactionExecutor.ExecuteAsync(
                request.PaymentId,
                Money.FromDecimal(request.Amount),
                NormalizeReason(request.Reason),
                attemptId,
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
    }

    private static RefundExecutionResult BuildReplayResult(PaymentAttemptData attempt)
    {
        if (attempt.ResponseStatusCode is null || attempt.ResponsePayload is null)
        {
            throw new InvalidOperationException(
                "Completed payment attempt does not contain a response.");
        }

        return new RefundExecutionResult(
            Success: attempt.ResponseStatusCode == 200,
            ResponseStatusCode: attempt.ResponseStatusCode.Value,
            ResponsePayload: attempt.ResponsePayload,
            OrderId: TryGetOrderId(attempt.ResponsePayload) ?? Guid.Empty,
            PaymentId: attempt.PaymentId ?? Guid.Empty,
            DomainEvents: Array.Empty<object>());
    }

    private async Task<RefundExecutionResult> PublishAndReturnAsync(
        RefundExecutionResult result,
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

    private static string? NormalizeReason(string? reason)
    {
        return string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
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

using MediatR;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.Application.Checkout.Commands;

public class CheckoutHandler : IRequestHandler<CheckoutCommand, CheckoutExecutionResult>
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan BoundedWaitTimeout = TimeSpan.FromSeconds(3);

    private readonly ICheckoutAttemptRepository _checkoutAttemptRepository;
    private readonly ICheckoutTransactionExecutor _checkoutTransactionExecutor;
    private readonly IOrderRepository _orderRepository;
    private readonly ICurrentUser _currentUser;

    public CheckoutHandler(
        ICheckoutAttemptRepository checkoutAttemptRepository,
        ICheckoutTransactionExecutor checkoutTransactionExecutor,
        IOrderRepository orderRepository,
        ICurrentUser currentUser)
    {
        _checkoutAttemptRepository = checkoutAttemptRepository;
        _checkoutTransactionExecutor = checkoutTransactionExecutor;
        _orderRepository = orderRepository;
        _currentUser = currentUser;
    }

    public async Task<CheckoutExecutionResult> Handle(
        CheckoutCommand request,
        CancellationToken cancellationToken)
    {
        var userId = _currentUser.UserId;
        var utcNow = DateTime.UtcNow;

        if (request.IfMatch is null)
        {
            throw new DomainConflictException(
                "The If-Match header is required for this operation.",
                "precondition_required");
        }

        var existing = await _checkoutAttemptRepository.GetByUserAndKeyAsync(
            userId,
            request.IdempotencyKey,
            cancellationToken);

        if (existing is not null)
        {
            return await HandleExistingAttemptAsync(existing, request, userId, cancellationToken);
        }

        var reserved = await _checkoutAttemptRepository.ReserveAsync(
            userId,
            request.IdempotencyKey,
            request.RequestFingerprint,
            utcNow,
            LeaseDuration,
            cancellationToken);

        if (reserved is null)
        {
            var raced = await _checkoutAttemptRepository.GetByUserAndKeyAsync(
                userId,
                request.IdempotencyKey,
                cancellationToken);

            if (raced is null)
            {
                throw new InvalidOperationException(
                    "Reservation failed but no existing attempt was found.");
            }

            return await HandleExistingAttemptAsync(raced, request, userId, cancellationToken);
        }

        return await _checkoutTransactionExecutor.ExecuteAsync(
            userId,
            reserved.Id,
            request.IfMatch,
            utcNow,
            cancellationToken);
    }

    private async Task<CheckoutExecutionResult> HandleExistingAttemptAsync(
        CheckoutAttemptData attempt,
        CheckoutCommand request,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (!attempt.RequestFingerprint.SequenceEqual(request.RequestFingerprint))
        {
            throw new DomainConflictException(
                "Idempotency key was reused with a different request.",
                "idempotency_key_reused");
        }

        if (attempt.State == CheckoutAttemptState.InProgress)
        {
            var completed = await _checkoutAttemptRepository.WaitForCompletionAsync(
                userId,
                request.IdempotencyKey,
                BoundedWaitTimeout,
                cancellationToken);

            if (completed is not null)
            {
                return await BuildReplayResultAsync(completed, cancellationToken);
            }

            var utcNow = DateTime.UtcNow;

            if (attempt.LeaseExpiresAtUtc > utcNow)
            {
                throw new DomainConflictException(
                    "A checkout attempt with this idempotency key is already in progress.",
                    "idempotency_in_progress");
            }

            var claimed = await _checkoutAttemptRepository.TryClaimExpiredAsync(
                attempt.Id,
                utcNow,
                LeaseDuration,
                cancellationToken);

            if (!claimed)
            {
                throw new DomainConflictException(
                    "A checkout attempt with this idempotency key is already in progress.",
                    "idempotency_in_progress");
            }

            var durableState = await _checkoutAttemptRepository.GetByUserAndKeyAsync(
                userId,
                request.IdempotencyKey,
                cancellationToken);

            if (durableState is null)
            {
                throw new InvalidOperationException(
                    "Checkout attempt disappeared after successful claim.");
            }

            if (durableState.State == CheckoutAttemptState.Completed)
            {
                return await BuildReplayResultAsync(durableState, cancellationToken);
            }

            if (durableState.OrderId is not null)
            {
                throw new InvalidOperationException(
                    "Inconsistent checkout attempt state: InProgress with OrderId.");
            }

            return await _checkoutTransactionExecutor.ExecuteAsync(
                userId,
                durableState.Id,
                request.IfMatch,
                utcNow,
                cancellationToken);
        }

        return await BuildReplayResultAsync(attempt, cancellationToken);
    }

    private async Task<CheckoutExecutionResult> BuildReplayResultAsync(
        CheckoutAttemptData attempt,
        CancellationToken cancellationToken)
    {
        byte[]? orderRowVersion = null;

        if (attempt.ResponseStatusCode == 201 && attempt.OrderId is not null)
        {
            orderRowVersion = await _orderRepository.GetRowVersionAsync(
                attempt.OrderId.Value,
                cancellationToken);
        }

        return new CheckoutExecutionResult(
            Success: attempt.ResponseStatusCode == 201,
            ResponseStatusCode: attempt.ResponseStatusCode!.Value,
            ResponsePayload: attempt.ResponsePayload!,
            OrderId: null,
            NewOrderRowVersion: orderRowVersion);
    }
}

using MediatR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Events;
using PS.MiniEcommerce.Application.Payments;
using PS.MiniEcommerce.Application.Payments.Commands;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.UnitTests.Application.Payments;

public class RefundPaymentHandlerTests
{
    private const string IdempotencyKey = "valid-refund-key-1";
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid PaymentId = Guid.NewGuid();
    private static readonly Guid OrderId = Guid.NewGuid();
    private static readonly byte[] Fingerprint = new byte[32];
    private static readonly byte[] OtherFingerprint = Enumerable.Repeat((byte)0xFF, 32).ToArray();
    private static readonly DateTime FixedUtcNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_WhenAttemptExistsWithDifferentFingerprint_ThrowsIdempotencyKeyReused()
    {
        var repository = Substitute.For<IPaymentAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(PaymentAttemptState.InProgress, OtherFingerprint));
        var executor = Substitute.For<IRefundTransactionExecutor>();
        var handler = CreateHandler(repository, executor);

        var exception = await Assert.ThrowsAsync<DomainConflictException>(
            () => handler.Handle(CreateCommand(), CancellationToken.None));

        Assert.Equal("idempotency_key_reused", exception.Code);
        await executor.DidNotReceive().ExecuteAsync(
            Arg.Any<Guid>(),
            Arg.Any<Money>(),
            Arg.Any<string?>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenAttemptCompleted_ReturnsReplayResult()
    {
        const string payload = "{\"id\":\"6f1e2a44-0000-0000-0000-000000000000\",\"orderId\":\"11111111-1111-1111-1111-111111111111\"}";
        var repository = Substitute.For<IPaymentAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(PaymentAttemptState.Completed, statusCode: 200, payload: payload));
        var executor = Substitute.For<IRefundTransactionExecutor>();
        var handler = CreateHandler(repository, executor);

        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(200, result.ResponseStatusCode);
        Assert.Equal(payload, result.ResponsePayload);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), result.OrderId);
        await executor.DidNotReceive().ExecuteAsync(
            Arg.Any<Guid>(),
            Arg.Any<Money>(),
            Arg.Any<string?>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenAttemptInProgress_AndWaitTimesOut_ThrowsIdempotencyInProgress()
    {
        var repository = Substitute.For<IPaymentAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(PaymentAttemptState.InProgress));
        repository
            .WaitForCompletionAsync(UserId, IdempotencyKey, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((PaymentAttemptData?)null);
        var executor = Substitute.For<IRefundTransactionExecutor>();
        var handler = CreateHandler(repository, executor);

        var exception = await Assert.ThrowsAsync<DomainConflictException>(
            () => handler.Handle(CreateCommand(), CancellationToken.None));

        Assert.Equal("idempotency_in_progress", exception.Code);
        await executor.DidNotReceive().ExecuteAsync(
            Arg.Any<Guid>(),
            Arg.Any<Money>(),
            Arg.Any<string?>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenAttemptInProgress_AndWaitSucceeds_ReturnsReplay()
    {
        const string payload = "{\"id\":\"6f1e2a44-0000-0000-0000-000000000000\"}";
        var repository = Substitute.For<IPaymentAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(PaymentAttemptState.InProgress));
        repository
            .WaitForCompletionAsync(UserId, IdempotencyKey, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(PaymentAttemptState.Completed, statusCode: 200, payload: payload));
        var executor = Substitute.For<IRefundTransactionExecutor>();
        var handler = CreateHandler(repository, executor);

        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(payload, result.ResponsePayload);
        await executor.DidNotReceive().ExecuteAsync(
            Arg.Any<Guid>(),
            Arg.Any<Money>(),
            Arg.Any<string?>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenAttemptLeaseExpired_ClaimsAndExecutes()
    {
        var repository = Substitute.For<IPaymentAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(
                PaymentAttemptState.InProgress,
                leaseExpiresAtUtc: FixedUtcNow.AddMinutes(-1)));
        repository
            .WaitForCompletionAsync(UserId, IdempotencyKey, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((PaymentAttemptData?)null);
        repository
            .TryClaimExpiredAsync(
                Arg.Any<Guid>(),
                Arg.Any<DateTime>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        var expected = CreateSuccessResult();
        var executor = Substitute.For<IRefundTransactionExecutor>();
        executor
            .ExecuteAsync(
                PaymentId,
                Money.FromDecimal(5m),
                "damaged",
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var handler = CreateHandler(repository, executor);

        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        Assert.Equal(expected, result);
        await repository.Received(1).TryClaimExpiredAsync(
            Arg.Any<Guid>(),
            Arg.Any<DateTime>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
        await executor.Received(1).ExecuteAsync(
            PaymentId,
            Money.FromDecimal(5m),
            "damaged",
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenNoAttempt_ReservesExecutesAndPublishesEvents()
    {
        var repository = Substitute.For<IPaymentAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns((PaymentAttemptData?)null);
        repository
            .ReserveAsync(
                UserId,
                IdempotencyKey,
                Arg.Any<byte[]>(),
                Arg.Any<DateTime>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(PaymentAttemptState.InProgress));

        var paymentRefunded = new PaymentRefunded(
            PaymentId,
            OrderId,
            Money.FromDecimal(5m),
            FixedUtcNow,
            "damaged");
        var expected = CreateSuccessResult(new object[] { paymentRefunded });
        var executor = Substitute.For<IRefundTransactionExecutor>();
        executor
            .ExecuteAsync(
                PaymentId,
                Money.FromDecimal(5m),
                "damaged",
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(expected);
        var mediator = Substitute.For<IMediator>();
        var handler = CreateHandler(repository, executor, mediator);

        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        Assert.Equal(expected, result);
        await repository.Received(1).ReserveAsync(
            UserId,
            IdempotencyKey,
            Fingerprint,
            Arg.Any<DateTime>(),
            TimeSpan.FromSeconds(30),
            Arg.Any<CancellationToken>());
        await mediator.Received(1).Publish(
            Arg.Is<DomainEventNotification<object>>(notification =>
                ReferenceEquals(notification.Event, paymentRefunded)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenExecutorThrowsRefundNotAllowed_PersistsErrorAndRethrows()
    {
        var repository = Substitute.For<IPaymentAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns((PaymentAttemptData?)null);
        repository
            .ReserveAsync(
                UserId,
                IdempotencyKey,
                Arg.Any<byte[]>(),
                Arg.Any<DateTime>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(PaymentAttemptState.InProgress));
        var executor = Substitute.For<IRefundTransactionExecutor>();
        executor
            .ExecuteAsync(
                PaymentId,
                Arg.Any<Money>(),
                Arg.Any<string?>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<RefundExecutionResult>(
                new DomainConflictException(
                    "Only confirmed payments can be refunded.",
                    "refund_not_allowed")));
        var handler = CreateHandler(repository, executor);

        var exception = await Assert.ThrowsAsync<DomainConflictException>(
            () => handler.Handle(CreateCommand(), CancellationToken.None));

        Assert.Equal("refund_not_allowed", exception.Code);
        await repository.Received(1).CompleteWithErrorAsync(
            Arg.Any<Guid>(),
            409,
            Arg.Is<string>(payload => payload.Contains("refund_not_allowed")),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenExecutorThrowsPaymentNotFound_Persists404AndRethrows()
    {
        var repository = Substitute.For<IPaymentAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns((PaymentAttemptData?)null);
        repository
            .ReserveAsync(
                UserId,
                IdempotencyKey,
                Arg.Any<byte[]>(),
                Arg.Any<DateTime>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(PaymentAttemptState.InProgress));
        var executor = Substitute.For<IRefundTransactionExecutor>();
        executor
            .ExecuteAsync(
                PaymentId,
                Arg.Any<Money>(),
                Arg.Any<string?>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<RefundExecutionResult>(
                new PS.MiniEcommerce.Application.Exceptions.NotFoundException(
                    "Payment not found.",
                    "payment_not_found")));
        var handler = CreateHandler(repository, executor);

        var exception = await Assert.ThrowsAsync<PS.MiniEcommerce.Application.Exceptions.NotFoundException>(
            () => handler.Handle(CreateCommand(), CancellationToken.None));

        Assert.Equal("payment_not_found", exception.Code);
        await repository.Received(1).CompleteWithErrorAsync(
            Arg.Any<Guid>(),
            404,
            Arg.Is<string>(payload => payload.Contains("payment_not_found")),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenExecutorThrowsInfrastructureError_DoesNotPersistError()
    {
        var repository = Substitute.For<IPaymentAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns((PaymentAttemptData?)null);
        repository
            .ReserveAsync(
                UserId,
                IdempotencyKey,
                Arg.Any<byte[]>(),
                Arg.Any<DateTime>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(PaymentAttemptState.InProgress));
        var executor = Substitute.For<IRefundTransactionExecutor>();
        executor
            .ExecuteAsync(
                PaymentId,
                Arg.Any<Money>(),
                Arg.Any<string?>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException<RefundExecutionResult>(
                new InvalidOperationException("Executor failed.")));
        var handler = CreateHandler(repository, executor);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Handle(CreateCommand(), CancellationToken.None));

        await repository.DidNotReceive().CompleteWithErrorAsync(
            Arg.Any<Guid>(),
            Arg.Any<int>(),
            Arg.Any<string>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    private static RefundPaymentCommand CreateCommand() =>
        new(PaymentId, 5m, "  damaged  ", IdempotencyKey, Fingerprint);

    private static RefundPaymentHandler CreateHandler(
        IPaymentAttemptRepository repository,
        IRefundTransactionExecutor executor,
        IMediator? mediator = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(UserId);

        return new RefundPaymentHandler(
            repository,
            executor,
            currentUser,
            mediator ?? Substitute.For<IMediator>(),
            Substitute.For<ILogger<RefundPaymentHandler>>());
    }

    private static PaymentAttemptData CreateAttempt(
        PaymentAttemptState state,
        byte[]? fingerprint = null,
        int? statusCode = null,
        string? payload = null,
        DateTime? leaseExpiresAtUtc = null) => new(
            Guid.NewGuid(),
            UserId,
            IdempotencyKey,
            fingerprint ?? Fingerprint,
            state,
            state == PaymentAttemptState.Completed ? PaymentId : null,
            statusCode,
            payload,
            leaseExpiresAtUtc ?? FixedUtcNow.AddSeconds(30));

    private static RefundExecutionResult CreateSuccessResult(
        IReadOnlyList<object>? domainEvents = null) => new(
            Success: true,
            ResponseStatusCode: 200,
            ResponsePayload: "{\"id\":\"6f1e2a44-0000-0000-0000-000000000000\"}",
            OrderId: OrderId,
            PaymentId: PaymentId,
            DomainEvents: domainEvents ?? Array.Empty<object>());
}

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

public class ConfirmPaymentHandlerTests
{
    private const string IdempotencyKey = "valid-key-12345";
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
        var executor = Substitute.For<IConfirmTransactionExecutor>();
        var handler = CreateHandler(repository, executor);

        var exception = await Assert.ThrowsAsync<DomainConflictException>(
            () => handler.Handle(CreateCommand(), CancellationToken.None));

        Assert.Equal("idempotency_key_reused", exception.Code);
        await executor.DidNotReceive().ExecuteAsync(
            Arg.Any<Guid>(),
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
        var executor = Substitute.For<IConfirmTransactionExecutor>();
        var handler = CreateHandler(repository, executor);

        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(200, result.ResponseStatusCode);
        Assert.Equal(payload, result.ResponsePayload);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), result.OrderId);
        await executor.DidNotReceive().ExecuteAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenAttemptCompletedWithPaymentFailed_ReopensAndExecutes()
    {
        const string payload = "{\"code\":\"payment_failed\"}";
        var repository = Substitute.For<IPaymentAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(PaymentAttemptState.Completed, statusCode: 409, payload: payload));
        repository
            .TryReopenFailedAsync(
                Arg.Any<Guid>(),
                Arg.Any<DateTime>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        var executor = Substitute.For<IConfirmTransactionExecutor>();
        var expected = CreateSuccessResult();
        executor
            .ExecuteAsync(PaymentId, UserId, Arg.Any<CancellationToken>())
            .Returns(expected);
        var handler = CreateHandler(repository, executor);

        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        Assert.Equal(expected, result);
        await repository.Received(1).TryReopenFailedAsync(
            Arg.Any<Guid>(),
            Arg.Any<DateTime>(),
            Arg.Any<TimeSpan>(),
            Arg.Any<CancellationToken>());
        await executor.Received(1).ExecuteAsync(PaymentId, UserId, Arg.Any<CancellationToken>());
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
        var executor = Substitute.For<IConfirmTransactionExecutor>();
        var handler = CreateHandler(repository, executor);

        var exception = await Assert.ThrowsAsync<DomainConflictException>(
            () => handler.Handle(CreateCommand(), CancellationToken.None));

        Assert.Equal("idempotency_in_progress", exception.Code);
        await executor.DidNotReceive().ExecuteAsync(
            Arg.Any<Guid>(),
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
        var executor = Substitute.For<IConfirmTransactionExecutor>();
        var handler = CreateHandler(repository, executor);

        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(payload, result.ResponsePayload);
        await executor.DidNotReceive().ExecuteAsync(
            Arg.Any<Guid>(),
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

        var paymentConfirmed = new PaymentConfirmed(
            PaymentId,
            OrderId,
            Money.FromDecimal(10m),
            FixedUtcNow);
        var expected = CreateSuccessResult(new object[] { paymentConfirmed });
        var executor = Substitute.For<IConfirmTransactionExecutor>();
        executor
            .ExecuteAsync(PaymentId, UserId, Arg.Any<CancellationToken>())
            .Returns(expected);
        var mediator = Substitute.For<IMediator>();
        var handler = CreateHandler(repository, executor, mediator);

        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        Assert.Equal(expected, result);
        await mediator.Received(1).Publish(
            Arg.Is<DomainEventNotification<object>>(notification =>
                ReferenceEquals(notification.Event, paymentConfirmed)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenExecutorThrowsBusinessError_PersistsErrorAndRethrows()
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
        var executor = Substitute.For<IConfirmTransactionExecutor>();
        executor
            .ExecuteAsync(PaymentId, UserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ConfirmExecutionResult>(
                new DomainConflictException("Payment is already confirmed.", "payment_already_confirmed")));
        var handler = CreateHandler(repository, executor);

        var exception = await Assert.ThrowsAsync<DomainConflictException>(
            () => handler.Handle(CreateCommand(), CancellationToken.None));

        Assert.Equal("payment_already_confirmed", exception.Code);
        await repository.Received(1).CompleteWithErrorAsync(
            Arg.Any<Guid>(),
            409,
            Arg.Is<string>(payload => payload.Contains("payment_already_confirmed")),
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
        var executor = Substitute.For<IConfirmTransactionExecutor>();
        executor
            .ExecuteAsync(PaymentId, UserId, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ConfirmExecutionResult>(
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

    private static ConfirmPaymentCommand CreateCommand() =>
        new(PaymentId, IdempotencyKey, Fingerprint);

    private static ConfirmPaymentHandler CreateHandler(
        IPaymentAttemptRepository repository,
        IConfirmTransactionExecutor executor,
        IMediator? mediator = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(UserId);

        return new ConfirmPaymentHandler(
            repository,
            executor,
            currentUser,
            mediator ?? Substitute.For<IMediator>(),
            Substitute.For<ILogger<ConfirmPaymentHandler>>());
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

    private static ConfirmExecutionResult CreateSuccessResult(
        IReadOnlyList<object>? domainEvents = null) => new(
            Success: true,
            ResponseStatusCode: 200,
            ResponsePayload: "{\"id\":\"6f1e2a44-0000-0000-0000-000000000000\"}",
            OrderId: OrderId,
            PaymentId: PaymentId,
            DomainEvents: domainEvents ?? Array.Empty<object>());
}

using NSubstitute;
using PS.MiniEcommerce.Application.Abstractions.CurrentUser;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Checkout;
using PS.MiniEcommerce.Application.Checkout.Commands;
using PS.MiniEcommerce.Domain.Exceptions;

namespace PS.MiniEcommerce.UnitTests.Application.Checkout;

public class CheckoutHandlerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private const string IdempotencyKey = "valid-key-12345";
    private static readonly byte[] Fingerprint = new byte[32];
    private static readonly byte[] OtherFingerprint = Enumerable.Repeat((byte)0xFF, 32).ToArray();
    private const string IfMatch = "\"AAAAAAAAB9Q=\"";
    private static readonly DateTime FixedUtcNow = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_WhenAttemptExistsWithDifferentFingerprint_ThrowsDomainConflictWithIdempotencyKeyReused()
    {
        var repository = Substitute.For<ICheckoutAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(CheckoutAttemptState.InProgress, OtherFingerprint));
        var executor = Substitute.For<ICheckoutTransactionExecutor>();
        var handler = CreateHandler(repository, executor);

        var exception = await Assert.ThrowsAsync<DomainConflictException>(
            () => handler.Handle(CreateCommand(), CancellationToken.None));

        Assert.Equal("idempotency_key_reused", exception.Code);
    }

    [Fact]
    public async Task Handle_WhenAttemptExistsCompleted_ReturnsReplayResult()
    {
        const string payload = "{\"id\":\"6f1e2a44-0000-0000-0000-000000000000\"}";
        var repository = Substitute.For<ICheckoutAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(CheckoutAttemptState.Completed, statusCode: 201, payload: payload));
        var executor = Substitute.For<ICheckoutTransactionExecutor>();
        var orderRepository = Substitute.For<IOrderRepository>();
        orderRepository
            .GetRowVersionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);
        var handler = CreateHandler(repository, executor, orderRepository);

        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(201, result.ResponseStatusCode);
        Assert.Equal(payload, result.ResponsePayload);
        Assert.Null(result.OrderId);
        Assert.Null(result.NewOrderRowVersion);
    }

    [Fact]
    public async Task Handle_WhenAttemptExistsCompletedWithOrder_ReturnsReplayWithCurrentOrderRowVersion()
    {
        const string payload = "{\"id\":\"6f1e2a44-0000-0000-0000-000000000000\"}";
        var orderId = Guid.NewGuid();
        var rowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var repository = Substitute.For<ICheckoutAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(CheckoutAttemptState.Completed, statusCode: 201, payload: payload, orderId: orderId));
        var executor = Substitute.For<ICheckoutTransactionExecutor>();
        var orderRepository = Substitute.For<IOrderRepository>();
        orderRepository
            .GetRowVersionAsync(orderId, Arg.Any<CancellationToken>())
            .Returns(rowVersion);
        var handler = CreateHandler(repository, executor, orderRepository);

        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(rowVersion, result.NewOrderRowVersion);
        await orderRepository.Received(1).GetRowVersionAsync(orderId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenAttemptExistsCompletedWithError_ReturnsErrorReplay()
    {
        const string payload = "{\"code\":\"cart_empty\"}";
        var repository = Substitute.For<ICheckoutAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(CheckoutAttemptState.Completed, statusCode: 409, payload: payload));
        var executor = Substitute.For<ICheckoutTransactionExecutor>();
        var handler = CreateHandler(repository, executor);

        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(409, result.ResponseStatusCode);
        Assert.Equal(payload, result.ResponsePayload);
    }

    [Fact]
    public async Task Handle_WhenAttemptExistsInProgress_AndWaitTimesOut_ThrowsIdempotencyInProgress()
    {
        var repository = Substitute.For<ICheckoutAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(CheckoutAttemptState.InProgress));
        repository
            .WaitForCompletionAsync(UserId, IdempotencyKey, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((CheckoutAttemptData?)null);
        var executor = Substitute.For<ICheckoutTransactionExecutor>();
        var handler = CreateHandler(repository, executor);

        var exception = await Assert.ThrowsAsync<DomainConflictException>(
            () => handler.Handle(CreateCommand(), CancellationToken.None));

        Assert.Equal("idempotency_in_progress", exception.Code);
        await executor.DidNotReceive().ExecuteAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string?>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenAttemptExistsInProgress_AndWaitSucceeds_ReturnsReplay()
    {
        const string payload = "{\"id\":\"6f1e2a44-0000-0000-0000-000000000000\"}";
        var repository = Substitute.For<ICheckoutAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(CheckoutAttemptState.InProgress));
        repository
            .WaitForCompletionAsync(UserId, IdempotencyKey, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(CheckoutAttemptState.Completed, statusCode: 201, payload: payload));
        var executor = Substitute.For<ICheckoutTransactionExecutor>();
        var handler = CreateHandler(repository, executor);

        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(201, result.ResponseStatusCode);
        Assert.Equal(payload, result.ResponsePayload);
        await executor.DidNotReceive().ExecuteAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string?>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenNoAttempt_ReservesAndExecutes()
    {
        var repository = Substitute.For<ICheckoutAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns((CheckoutAttemptData?)null);
        repository
            .ReserveAsync(
                UserId,
                IdempotencyKey,
                Arg.Any<byte[]>(),
                Arg.Any<DateTime>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(CheckoutAttemptState.InProgress));

        var mockResult = new CheckoutExecutionResult(
            Success: true,
            ResponseStatusCode: 201,
            ResponsePayload: "{\"status\":\"Created\"}",
            OrderId: Guid.NewGuid(),
            NewOrderRowVersion: new byte[8]);
        var executor = Substitute.For<ICheckoutTransactionExecutor>();
        executor
            .ExecuteAsync(UserId, Arg.Any<Guid>(), IfMatch, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(mockResult);
        var handler = CreateHandler(repository, executor);

        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        await executor.Received(1).ExecuteAsync(
            UserId,
            Arg.Any<Guid>(),
            IfMatch,
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        Assert.Equal(mockResult, result);
    }

    [Fact]
    public async Task Handle_WhenReserveReturnsNull_AndWaitTimesOut_ThrowsIdempotencyInProgress()
    {
        var repository = Substitute.For<ICheckoutAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns((CheckoutAttemptData?)null, CreateAttempt(CheckoutAttemptState.InProgress));
        repository
            .ReserveAsync(
                UserId,
                IdempotencyKey,
                Arg.Any<byte[]>(),
                Arg.Any<DateTime>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns((CheckoutAttemptData?)null);
        repository
            .WaitForCompletionAsync(UserId, IdempotencyKey, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((CheckoutAttemptData?)null);
        var executor = Substitute.For<ICheckoutTransactionExecutor>();
        var handler = CreateHandler(repository, executor);

        var exception = await Assert.ThrowsAsync<DomainConflictException>(
            () => handler.Handle(CreateCommand(), CancellationToken.None));

        Assert.Equal("idempotency_in_progress", exception.Code);
        await executor.DidNotReceive().ExecuteAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string?>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenReserveReturnsNull_AndWaitSucceeds_ReturnsReplay()
    {
        const string payload = "{\"id\":\"6f1e2a44-0000-0000-0000-000000000000\"}";
        var repository = Substitute.For<ICheckoutAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns((CheckoutAttemptData?)null, CreateAttempt(CheckoutAttemptState.InProgress));
        repository
            .ReserveAsync(
                UserId,
                IdempotencyKey,
                Arg.Any<byte[]>(),
                Arg.Any<DateTime>(),
                Arg.Any<TimeSpan>(),
                Arg.Any<CancellationToken>())
            .Returns((CheckoutAttemptData?)null);
        repository
            .WaitForCompletionAsync(UserId, IdempotencyKey, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(CheckoutAttemptState.Completed, statusCode: 201, payload: payload));
        var executor = Substitute.For<ICheckoutTransactionExecutor>();
        var handler = CreateHandler(repository, executor);

        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(201, result.ResponseStatusCode);
        Assert.Equal(payload, result.ResponsePayload);
        await executor.DidNotReceive().ExecuteAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string?>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenAttemptExistsInProgressLeaseExpired_ClaimsAndResumes()
    {
        var expired = CreateAttempt(
            CheckoutAttemptState.InProgress,
            leaseExpiresAtUtc: DateTime.UtcNow.AddSeconds(-10));
        var resumed = CreateAttempt(
            CheckoutAttemptState.InProgress,
            leaseExpiresAtUtc: DateTime.UtcNow.AddSeconds(30));

        var repository = Substitute.For<ICheckoutAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns(expired, resumed);
        repository
            .WaitForCompletionAsync(UserId, IdempotencyKey, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((CheckoutAttemptData?)null);
        repository
            .TryClaimExpiredAsync(expired.Id, Arg.Any<DateTime>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var mockResult = new CheckoutExecutionResult(
            Success: true,
            ResponseStatusCode: 201,
            ResponsePayload: "{\"status\":\"Created\"}",
            OrderId: Guid.NewGuid(),
            NewOrderRowVersion: new byte[8]);
        var executor = Substitute.For<ICheckoutTransactionExecutor>();
        executor
            .ExecuteAsync(UserId, resumed.Id, IfMatch, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(mockResult);

        var handler = CreateHandler(repository, executor);

        var result = await handler.Handle(CreateCommand(), CancellationToken.None);

        await executor.Received(1).ExecuteAsync(
            UserId,
            resumed.Id,
            IfMatch,
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
        Assert.Equal(mockResult, result);
    }

    [Fact]
    public async Task Handle_WhenAttemptExistsInProgressLeaseExpired_ClaimFails_ThrowsIdempotencyInProgress()
    {
        var repository = Substitute.For<ICheckoutAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(
                CheckoutAttemptState.InProgress,
                leaseExpiresAtUtc: DateTime.UtcNow.AddSeconds(-10)));
        repository
            .WaitForCompletionAsync(UserId, IdempotencyKey, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((CheckoutAttemptData?)null);
        repository
            .TryClaimExpiredAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var executor = Substitute.For<ICheckoutTransactionExecutor>();
        var handler = CreateHandler(repository, executor);

        var exception = await Assert.ThrowsAsync<DomainConflictException>(
            () => handler.Handle(CreateCommand(), CancellationToken.None));

        Assert.Equal("idempotency_in_progress", exception.Code);
        await executor.DidNotReceive().ExecuteAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string?>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WhenAttemptExistsInProgressLeaseActive_ThrowsIdempotencyInProgress()
    {
        var repository = Substitute.For<ICheckoutAttemptRepository>();
        repository
            .GetByUserAndKeyAsync(UserId, IdempotencyKey, Arg.Any<CancellationToken>())
            .Returns(CreateAttempt(
                CheckoutAttemptState.InProgress,
                leaseExpiresAtUtc: DateTime.UtcNow.AddSeconds(20)));
        repository
            .WaitForCompletionAsync(UserId, IdempotencyKey, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns((CheckoutAttemptData?)null);

        var executor = Substitute.For<ICheckoutTransactionExecutor>();
        var handler = CreateHandler(repository, executor);

        var exception = await Assert.ThrowsAsync<DomainConflictException>(
            () => handler.Handle(CreateCommand(), CancellationToken.None));

        Assert.Equal("idempotency_in_progress", exception.Code);
        await executor.DidNotReceive().ExecuteAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<string?>(),
            Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>());
    }

    private static CheckoutCommand CreateCommand() => new(IdempotencyKey, IfMatch, Fingerprint);

    private static CheckoutHandler CreateHandler(
        ICheckoutAttemptRepository repository,
        ICheckoutTransactionExecutor executor,
        IOrderRepository? orderRepository = null)
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.UserId.Returns(UserId);

        return new CheckoutHandler(
            repository,
            executor,
            orderRepository ?? Substitute.For<IOrderRepository>(),
            currentUser);
    }

    private static CheckoutAttemptData CreateAttempt(
        CheckoutAttemptState state,
        byte[]? fingerprint = null,
        int? statusCode = null,
        string? payload = null,
        DateTime? leaseExpiresAtUtc = null,
        Guid? orderId = null) => new(
            Guid.NewGuid(),
            UserId,
            IdempotencyKey,
            fingerprint ?? Fingerprint,
            state,
            orderId ?? (state == CheckoutAttemptState.Completed ? Guid.NewGuid() : null),
            statusCode,
            payload,
            leaseExpiresAtUtc ?? FixedUtcNow.AddSeconds(30));
}

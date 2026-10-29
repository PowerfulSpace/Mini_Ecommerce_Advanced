using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Application.Abstractions.Outbox;
using PS.MiniEcommerce.Application.Abstractions.Payments;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Exceptions;
using PS.MiniEcommerce.Application.Payments;
using PS.MiniEcommerce.Application.Payments.DTOs;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Domain.Exceptions;
using PS.MiniEcommerce.Domain.Payments;
using PS.MiniEcommerce.Infrastructure.Persistence;

namespace PS.MiniEcommerce.Infrastructure.Payments;

public class ConfirmTransactionExecutor : IConfirmTransactionExecutor
{
    private const int OkStatusCode = 200;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _dbContext;
    private readonly IOrderRepository _orderRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentProvider _paymentProvider;
    private readonly ConfirmAttemptContext _confirmAttemptContext;
    private readonly IOutboxRepository _outboxRepository;

    public ConfirmTransactionExecutor(
        AppDbContext dbContext,
        IOrderRepository orderRepository,
        IPaymentRepository paymentRepository,
        IPaymentProvider paymentProvider,
        ConfirmAttemptContext confirmAttemptContext,
        IOutboxRepository outboxRepository)
    {
        _dbContext = dbContext;
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentProvider = paymentProvider;
        _confirmAttemptContext = confirmAttemptContext;
        _outboxRepository = outboxRepository;
    }

    public async Task<ConfirmExecutionResult> ExecuteAsync(
        Guid paymentId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var paymentAttemptId = _confirmAttemptContext.PaymentAttemptId
            ?? throw new InvalidOperationException(
                "Payment attempt context is required to confirm a payment.");

        var utcNow = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        Payment? payment = null;
        Order? order = null;
        string? responsePayload = null;
        var paymentFailed = false;
        var domainEvents = new List<object>();

        try
        {
            var orderId = await _paymentRepository.GetOrderIdAsync(paymentId, cancellationToken);

            if (orderId is null)
            {
                throw new NotFoundException("Payment not found.", "payment_not_found");
            }

            order = await _dbContext.Orders
                .FromSqlInterpolated($"SELECT * FROM Orders WITH (UPDLOCK) WHERE Id = {orderId.Value}")
                .FirstOrDefaultAsync(cancellationToken);

            if (order is null)
            {
                throw new NotFoundException("Payment not found.", "payment_not_found");
            }

            payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);

            if (payment is null || payment.OrderId != order.Id || order.UserId != userId)
            {
                throw new NotFoundException("Payment not found.", "payment_not_found");
            }

            if (payment.Status == PaymentStatus.Confirmed && _confirmAttemptContext.IsRetry)
            {
                responsePayload = JsonSerializer.Serialize(payment.ToDto(), JsonOptions);

                var completedAttempt = await _dbContext.PaymentAttempts
                    .FirstAsync(
                        paymentAttempt => paymentAttempt.Id == paymentAttemptId,
                        cancellationToken);

                completedAttempt.MarkCompleted(
                    payment.Id,
                    OkStatusCode,
                    responsePayload,
                    utcNow);

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return new ConfirmExecutionResult(
                    Success: true,
                    ResponseStatusCode: OkStatusCode,
                    ResponsePayload: responsePayload,
                    OrderId: order.Id,
                    PaymentId: payment.Id,
                    DomainEvents: Array.Empty<object>());
            }

            if (payment.Status is PaymentStatus.Confirmed or PaymentStatus.Refunded)
            {
                throw new DomainConflictException(
                    "Payment is already confirmed.",
                    "payment_already_confirmed");
            }

            if (order.Status != OrderStatus.Created)
            {
                throw new DomainConflictException(
                    $"Cannot transition order from '{order.Status}' to '{OrderStatus.Paid}'.",
                    "invalid_order_transition");
            }

            if (payment.Status == PaymentStatus.Failed)
            {
                payment.MarkPending();
            }

            var providerResult = await _paymentProvider.ConfirmAsync(paymentId, cancellationToken);

            if (!providerResult.Success)
            {
                payment.MarkFailed();

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                paymentFailed = true;
            }
            else
            {
                payment.MarkConfirmed(utcNow);

                await _orderRepository.ConsumeReservedAsync(
                    order.Id,
                    cancellationToken,
                    useOwnTransaction: false);

                order.MarkPaid(utcNow);

                domainEvents.Add(new PaymentConfirmed(
                    payment.Id,
                    payment.OrderId,
                    payment.Amount,
                    utcNow));
                domainEvents.AddRange(order.DomainEvents);

                foreach (var domainEvent in domainEvents)
                {
                    await _outboxRepository.AddAsync(domainEvent, utcNow, cancellationToken);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);

                responsePayload = JsonSerializer.Serialize(payment.ToDto(), JsonOptions);

                var attempt = await _dbContext.PaymentAttempts
                    .FirstAsync(
                        paymentAttempt => paymentAttempt.Id == paymentAttemptId,
                        cancellationToken);

                attempt.MarkCompleted(payment.Id, OkStatusCode, responsePayload, utcNow);

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (Exception exception) when (exception is DomainException or NotFoundException or ValidationException)
        {
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
            throw;
        }

        if (paymentFailed)
        {
            throw new DomainConflictException(
                "Payment confirmation failed.",
                "payment_failed");
        }

        if (payment is null || order is null || responsePayload is null)
        {
            throw new InvalidOperationException("Payment confirmation did not produce a result.");
        }

        order.ClearDomainEvents();

        return new ConfirmExecutionResult(
            Success: true,
            ResponseStatusCode: OkStatusCode,
            ResponsePayload: responsePayload,
            OrderId: order.Id,
            PaymentId: payment.Id,
            DomainEvents: domainEvents);
    }
}

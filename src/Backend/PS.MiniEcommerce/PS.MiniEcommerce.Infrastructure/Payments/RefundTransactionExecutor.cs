using System.Text.Json;
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

public class RefundTransactionExecutor : IRefundTransactionExecutor
{
    private const int OkStatusCode = 200;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _dbContext;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentAttemptRepository _paymentAttemptRepository;
    private readonly IPaymentRefundService _paymentRefundService;

    public RefundTransactionExecutor(
        AppDbContext dbContext,
        IPaymentRepository paymentRepository,
        IPaymentAttemptRepository paymentAttemptRepository,
        IPaymentRefundService paymentRefundService)
    {
        _dbContext = dbContext;
        _paymentRepository = paymentRepository;
        _paymentAttemptRepository = paymentAttemptRepository;
        _paymentRefundService = paymentRefundService;
    }

    public async Task<RefundExecutionResult> ExecuteAsync(
        Guid paymentId,
        Money amount,
        string? reason,
        Guid paymentAttemptId,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        Payment? payment;

        try
        {
            payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);

            if (payment is null)
            {
                throw new NotFoundException("Payment not found.", "payment_not_found");
            }

            await _paymentRefundService.RefundAsync(
                payment,
                amount,
                reason,
                utcNow,
                cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);

            var responsePayload = JsonSerializer.Serialize(payment.ToDto(), JsonOptions);

            await _paymentAttemptRepository.CompleteAsync(
                paymentAttemptId,
                payment.Id,
                OkStatusCode,
                responsePayload,
                utcNow,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new RefundExecutionResult(
                Success: true,
                ResponseStatusCode: OkStatusCode,
                ResponsePayload: responsePayload,
                OrderId: payment.OrderId,
                PaymentId: payment.Id,
                DomainEvents: new object[]
                {
                    new PaymentRefunded(payment.Id, payment.OrderId, amount, utcNow, reason)
                });
        }
        catch (Exception exception) when (exception is DomainException or NotFoundException or ValidationException)
        {
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }
}

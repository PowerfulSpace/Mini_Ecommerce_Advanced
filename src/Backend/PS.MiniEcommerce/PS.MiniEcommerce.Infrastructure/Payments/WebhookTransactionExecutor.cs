using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Application.Abstractions.Outbox;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Application.Webhooks;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Domain.Payments;
using PS.MiniEcommerce.Infrastructure.Persistence;

namespace PS.MiniEcommerce.Infrastructure.Payments;

public sealed class WebhookTransactionExecutor : IWebhookTransactionExecutor
{
    private static readonly WebhookExecutionResult NoOp =
        new(Processed: false, DomainEvents: Array.Empty<object>());

    private readonly AppDbContext _dbContext;
    private readonly IOrderRepository _orderRepository;
    private readonly IOutboxRepository _outboxRepository;

    public WebhookTransactionExecutor(
        AppDbContext dbContext,
        IOrderRepository orderRepository,
        IOutboxRepository outboxRepository)
    {
        _dbContext = dbContext;
        _orderRepository = orderRepository;
        _outboxRepository = outboxRepository;
    }

    public async Task<WebhookExecutionResult> ExecutePaymentIntentSucceededAsync(
        string eventId,
        string eventType,
        Guid paymentId,
        Money amount,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            _dbContext.ChangeTracker.Clear();

            var payment = await _dbContext.Payments
                .FromSqlInterpolated($"SELECT * FROM Payments WITH (UPDLOCK) WHERE Id = {paymentId}")
                .FirstOrDefaultAsync(cancellationToken);

            if (payment is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return NoOp;
            }

            var order = await _dbContext.Orders
                .FromSqlInterpolated($"SELECT * FROM Orders WITH (UPDLOCK) WHERE Id = {payment.OrderId}")
                .FirstOrDefaultAsync(cancellationToken);

            if (payment.Status != PaymentStatus.Pending
                || payment.Amount != amount
                || order is null
                || order.Status != OrderStatus.Created)
            {
                _dbContext.ProcessedStripeEvents.Add(
                    ProcessedStripeEvent.Create(eventId, eventType, utcNow));

                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return NoOp;
            }

            payment.MarkConfirmed(utcNow);

            await _orderRepository.ConsumeReservedAsync(
                order.Id,
                cancellationToken,
                useOwnTransaction: false);

            order.MarkPaid(utcNow);

            var domainEvents = new List<object>
            {
                new PaymentConfirmed(
                    payment.Id,
                    payment.OrderId,
                    payment.Amount,
                    utcNow)
            };

            domainEvents.AddRange(order.DomainEvents);

            foreach (var domainEvent in domainEvents)
            {
                await _outboxRepository.AddAsync(domainEvent, utcNow, cancellationToken);
            }

            _dbContext.ProcessedStripeEvents.Add(
                ProcessedStripeEvent.Create(eventId, eventType, utcNow));

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            order.ClearDomainEvents();

            return new WebhookExecutionResult(Processed: true, DomainEvents: domainEvents);
        }
        catch (DbUpdateException exception) when (ProcessedStripeEventRepository.IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
            return NoOp;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
            throw;
        }
    }
}

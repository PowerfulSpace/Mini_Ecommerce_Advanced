using System.Collections.Concurrent;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PS.MiniEcommerce.Application.Events;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Domain.Payments;
using PS.MiniEcommerce.Infrastructure;

namespace PS.MiniEcommerce.IntegrationTests;

public sealed class NotificationCapture
{
    private readonly ConcurrentQueue<object> _events = new();

    public IReadOnlyList<object> Events => _events.ToArray();

    public bool? OrderPlacedWasVisibleAfterCommit { get; set; }

    public bool? OrderCancelledWasVisibleAfterCommit { get; set; }

    public bool? PaymentConfirmedWasVisibleAfterCommit { get; set; }

    public bool? PaymentRefundedWasVisibleAfterCommit { get; set; }

    public bool? OrderPaidWasVisibleAfterCommit { get; set; }

    public void Add(object domainEvent)
    {
        _events.Enqueue(domainEvent);
    }

    public void Clear()
    {
        while (_events.TryDequeue(out _))
        {
        }

        OrderPlacedWasVisibleAfterCommit = null;
        OrderCancelledWasVisibleAfterCommit = null;
        PaymentConfirmedWasVisibleAfterCommit = null;
        PaymentRefundedWasVisibleAfterCommit = null;
        OrderPaidWasVisibleAfterCommit = null;
    }
}

public sealed class CaptureNotificationHandler<T> : INotificationHandler<DomainEventNotification<T>>
    where T : class
{
    private readonly NotificationCapture _capture;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public CaptureNotificationHandler(
        NotificationCapture capture,
        IServiceScopeFactory serviceScopeFactory)
    {
        _capture = capture;
        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task Handle(DomainEventNotification<T> notification, CancellationToken cancellationToken)
    {
        _capture.Add(notification.Event);

        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        switch (notification.Event)
        {
            case OrderPlaced orderPlaced:
                _capture.OrderPlacedWasVisibleAfterCommit = await dbContext.Orders
                    .AsNoTracking()
                    .AnyAsync(order => order.Id == orderPlaced.OrderId, cancellationToken);
                break;

            case OrderCancelled orderCancelled:
                _capture.OrderCancelledWasVisibleAfterCommit = await dbContext.Orders
                    .AsNoTracking()
                    .AnyAsync(order => order.Id == orderCancelled.OrderId, cancellationToken);
                break;

            case PaymentConfirmed paymentConfirmed:
                var confirmedPayment = await dbContext.Payments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        payment => payment.Id == paymentConfirmed.PaymentId,
                        cancellationToken);
                var paidOrder = await dbContext.Orders
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        order => order.Id == paymentConfirmed.OrderId,
                        cancellationToken);

                _capture.PaymentConfirmedWasVisibleAfterCommit =
                    confirmedPayment?.Status == PaymentStatus.Confirmed
                    && paidOrder?.Status == OrderStatus.Paid;
                break;

            case PaymentRefunded paymentRefunded:
                var refundedPayment = await dbContext.Payments
                    .AsNoTracking()
                    .Include(payment => payment.Refunds)
                    .FirstOrDefaultAsync(
                        payment => payment.Id == paymentRefunded.PaymentId,
                        cancellationToken);

                var isFullyRefunded = refundedPayment is not null
                    && refundedPayment.RefundedAmount.Amount == refundedPayment.Amount.Amount;

                _capture.PaymentRefundedWasVisibleAfterCommit =
                    refundedPayment is not null
                    && refundedPayment.RefundedAmount.Amount >= paymentRefunded.Amount.Amount
                    && refundedPayment.Refunds.Any(
                        refund => refund.Amount.Amount == paymentRefunded.Amount.Amount)
                    && (isFullyRefunded
                        ? refundedPayment.Status == PaymentStatus.Refunded
                        : refundedPayment.Status == PaymentStatus.Confirmed);
                break;

            case OrderPaid orderPaid:
                var storedPaidOrder = await dbContext.Orders
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        order => order.Id == orderPaid.OrderId,
                        cancellationToken);

                _capture.OrderPaidWasVisibleAfterCommit =
                    storedPaidOrder?.Status == OrderStatus.Paid;
                break;
        }
    }
}

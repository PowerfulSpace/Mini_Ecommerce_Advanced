using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Application.Abstractions.Persistence;
using PS.MiniEcommerce.Domain.Payments;

namespace PS.MiniEcommerce.Infrastructure.Persistence;

public class PaymentRepository : IPaymentRepository
{
    private readonly AppDbContext _dbContext;

    public PaymentRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task AddAsync(Payment payment, CancellationToken cancellationToken)
    {
        _dbContext.Payments.Add(payment);
        return Task.CompletedTask;
    }

    public Task<Guid?> GetOrderIdAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        return _dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.Id == paymentId)
            .Select(payment => (Guid?)payment.OrderId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<Payment?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        return _dbContext.Payments
            .FromSqlInterpolated($"SELECT * FROM Payments WITH (UPDLOCK) WHERE Id = {paymentId}")
            .Include(payment => payment.Refunds)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task UpdateAsync(Payment payment, CancellationToken cancellationToken)
    {
        _dbContext.Payments.Update(payment);
        return Task.CompletedTask;
    }
}

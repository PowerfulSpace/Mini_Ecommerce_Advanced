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

    public Task<Payment?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        return _dbContext.Payments
            .Include(payment => payment.Refunds)
            .FirstOrDefaultAsync(payment => payment.Id == paymentId, cancellationToken);
    }

    public Task UpdateAsync(Payment payment, CancellationToken cancellationToken)
    {
        _dbContext.Payments.Update(payment);
        return Task.CompletedTask;
    }
}

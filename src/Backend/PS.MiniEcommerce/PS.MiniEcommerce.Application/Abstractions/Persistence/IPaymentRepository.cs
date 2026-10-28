using PS.MiniEcommerce.Domain.Payments;

namespace PS.MiniEcommerce.Application.Abstractions.Persistence;

public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken cancellationToken);

    Task<Guid?> GetOrderIdAsync(Guid paymentId, CancellationToken cancellationToken);

    Task<Payment?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken);

    Task UpdateAsync(Payment payment, CancellationToken cancellationToken);
}

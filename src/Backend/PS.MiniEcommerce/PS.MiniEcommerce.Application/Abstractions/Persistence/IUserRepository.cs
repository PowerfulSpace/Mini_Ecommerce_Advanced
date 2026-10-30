namespace PS.MiniEcommerce.Application.Abstractions.Persistence;

public interface IUserRepository
{
    Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken);
}

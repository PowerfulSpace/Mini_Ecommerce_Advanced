using Microsoft.EntityFrameworkCore;
using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Infrastructure.Seed;

public class DataSeeder
{
    private readonly AppDbContext _dbContext;

    public DataSeeder(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedUsersAsync(cancellationToken);
        await SeedProductsAsync(cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedUsersAsync(CancellationToken cancellationToken)
    {
        var existingEmails = await _dbContext.Users
            .Select(user => user.Email)
            .ToListAsync(cancellationToken);

        foreach (var seed in SeedData.GetUsers())
        {
            if (existingEmails.Contains(seed.Email))
            {
                continue;
            }

            _dbContext.Users.Add(User.Create(seed.Email, seed.Role, SeedData.SeedTimestampUtc));
        }
    }

    private async Task SeedProductsAsync(CancellationToken cancellationToken)
    {
        var existingNames = await _dbContext.Products
            .Select(product => product.Name)
            .ToListAsync(cancellationToken);

        foreach (var seed in SeedData.GetProducts())
        {
            if (existingNames.Contains(seed.Name))
            {
                continue;
            }

            var product = Product.Create(
                seed.Name,
                seed.Description,
                seed.Price,
                seed.Available,
                SeedData.SeedTimestampUtc,
                reserved: 0);

            if (!seed.IsActive)
            {
                product.Deactivate(SeedData.SeedTimestampUtc);
                product.ClearDomainEvents();
            }

            _dbContext.Products.Add(product);
        }
    }
}

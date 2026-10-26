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

        foreach (var email in SeedData.GetUserEmails())
        {
            if (existingEmails.Contains(email))
            {
                continue;
            }

            _dbContext.Users.Add(User.Create(email, SeedData.SeedTimestampUtc));
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
                seed.StockQuantity,
                SeedData.SeedTimestampUtc);

            if (!seed.IsActive)
            {
                product.Deactivate();
            }

            _dbContext.Products.Add(product);
        }
    }
}

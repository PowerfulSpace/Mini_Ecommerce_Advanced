using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Infrastructure.Persistence;

public class MoneyValueConverter : ValueConverter<Money, decimal>
{
    public MoneyValueConverter()
        : base(
            money => money.Amount,
            amount => Money.FromDecimal(amount))
    {
    }
}

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PS.MiniEcommerce.Infrastructure.Persistence;

public class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            value => value.Kind == DateTimeKind.Utc
                ? value
                : value.ToUniversalTime(),
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
    {
    }
}

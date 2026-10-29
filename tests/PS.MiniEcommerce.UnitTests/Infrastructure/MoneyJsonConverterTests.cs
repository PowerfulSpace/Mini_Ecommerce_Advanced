using System.Text.Json;
using PS.MiniEcommerce.Domain;
using PS.MiniEcommerce.Domain.Events;
using PS.MiniEcommerce.Infrastructure.Serialization;

namespace PS.MiniEcommerce.UnitTests.Infrastructure;

public class MoneyJsonConverterTests
{
    [Fact]
    public void Serialize_Money_WritesNumber()
    {
        var domainEvent = new OrderPlaced(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Money.FromDecimal(19.99m),
            1,
            DateTime.UtcNow);

        var payload = JsonSerializer.Serialize(
            domainEvent,
            domainEvent.GetType(),
            OutboxJsonOptions.Instance);

        using var document = JsonDocument.Parse(payload);
        Assert.Equal(19.99m, document.RootElement.GetProperty("totalAmount").GetDecimal());
    }

    [Fact]
    public void Deserialize_Money_RoundTripsAmount()
    {
        var domainEvent = new OrderPlaced(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Money.FromDecimal(123.45m),
            2,
            DateTime.UtcNow);

        var payload = JsonSerializer.Serialize(
            domainEvent,
            domainEvent.GetType(),
            OutboxJsonOptions.Instance);

        var restored = JsonSerializer.Deserialize<OrderPlaced>(
            payload,
            OutboxJsonOptions.Instance);

        Assert.NotNull(restored);
        Assert.Equal(123.45m, restored.TotalAmount.Amount);
    }
}

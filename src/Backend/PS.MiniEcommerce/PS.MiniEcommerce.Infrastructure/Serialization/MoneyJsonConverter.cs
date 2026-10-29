using System.Text.Json;
using System.Text.Json.Serialization;
using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Infrastructure.Serialization;

public class MoneyJsonConverter : JsonConverter<Money>
{
    public override Money Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw new JsonException("Expected a numeric value for Money.");
        }

        return Money.FromDecimal(reader.GetDecimal());
    }

    public override void Write(
        Utf8JsonWriter writer,
        Money value,
        JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.Amount);
    }
}

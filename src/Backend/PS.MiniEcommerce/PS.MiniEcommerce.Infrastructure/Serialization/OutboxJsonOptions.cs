using System.Text.Json;

namespace PS.MiniEcommerce.Infrastructure.Serialization;

public static class OutboxJsonOptions
{
    public static readonly JsonSerializerOptions Instance = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new MoneyJsonConverter());
        return options;
    }
}

using PS.MiniEcommerce.Domain;

namespace PS.MiniEcommerce.Infrastructure.Seed;

public static class SeedData
{
    public const string AliceEmail = "alice@example.com";

    public const string BobEmail = "bob@example.com";

    public static readonly DateTime SeedTimestampUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static IReadOnlyList<string> GetUserEmails() => new[]
    {
        AliceEmail,
        BobEmail
    };

    public static IReadOnlyList<ProductSeed> GetProducts() => new[]
    {
        new ProductSeed(
            "Wireless Mouse",
            "Compact wireless mouse with adjustable DPI settings.",
            19.99m,
            100,
            true),
        new ProductSeed(
            "Ergonomic Keyboard",
            "Split ergonomic keyboard with an integrated wrist support.",
            9.50m,
            50,
            true),
        new ProductSeed(
            "USB-C Cable",
            "Durable two-meter USB-C cable for charging and data transfer.",
            149.00m,
            25,
            true),
        new ProductSeed(
            "Laptop Stand",
            "Aluminium laptop stand with adjustable height.",
            4.99m,
            10,
            true),
        new ProductSeed(
            "Noise-Cancelling Headphones",
            "Over-ear headphones with active noise cancellation.",
            29.95m,
            200,
            true),
        new ProductSeed(
            "Webcam 1080p",
            "Full HD webcam with a built-in microphone.",
            12.00m,
            75,
            true),
        new ProductSeed(
            "Discontinued Tablet Case",
            "Protective case for a discontinued tablet model.",
            5.00m,
            100,
            false),
        new ProductSeed(
            "Out-of-Stock Monitor",
            "27-inch IPS monitor, currently unavailable.",
            39.99m,
            0,
            true),
        new ProductSeed(
            "Limited Edition Mug",
            "Limited edition ceramic mug, a single unit remains in stock.",
            99.99m,
            1,
            true)
    };
}

public sealed record ProductSeed(string Name, string Description, decimal Price, int StockQuantity, bool IsActive);

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
            Money.FromDecimal(19.99m),
            100,
            true),
        new ProductSeed(
            "Ergonomic Keyboard",
            "Split ergonomic keyboard with an integrated wrist support.",
            Money.FromDecimal(9.50m),
            50,
            true),
        new ProductSeed(
            "USB-C Cable",
            "Durable two-meter USB-C cable for charging and data transfer.",
            Money.FromDecimal(149.00m),
            25,
            true),
        new ProductSeed(
            "Laptop Stand",
            "Aluminium laptop stand with adjustable height.",
            Money.FromDecimal(4.99m),
            10,
            true),
        new ProductSeed(
            "Noise-Cancelling Headphones",
            "Over-ear headphones with active noise cancellation.",
            Money.FromDecimal(29.95m),
            200,
            true),
        new ProductSeed(
            "Webcam 1080p",
            "Full HD webcam with a built-in microphone.",
            Money.FromDecimal(12.00m),
            75,
            true),
        new ProductSeed(
            "Discontinued Tablet Case",
            "Protective case for a discontinued tablet model.",
            Money.FromDecimal(5.00m),
            100,
            false),
        new ProductSeed(
            "Out-of-Stock Monitor",
            "27-inch IPS monitor, currently unavailable.",
            Money.FromDecimal(39.99m),
            0,
            true),
        new ProductSeed(
            "Limited Edition Mug",
            "Limited edition ceramic mug, a single unit remains in stock.",
            Money.FromDecimal(99.99m),
            1,
            true)
    };
}

public sealed record ProductSeed(string Name, string Description, Money Price, int Available, bool IsActive);

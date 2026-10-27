namespace PS.MiniEcommerce.Domain;

// Negative amounts are intentionally allowed so that subtraction and differences work.
// Entities enforce non-negative amounts where the domain requires them.
public readonly record struct Money
{
    public static readonly string Currency = "USD";

    public decimal Amount { get; }

    private Money(decimal amount)
    {
        Amount = Round(amount);
    }

    public static Money Zero => new(0m);

    public static Money FromDecimal(decimal amount) => new(amount);

    public static Money operator +(Money a, Money b) => new(a.Amount + b.Amount);

    public static Money operator -(Money a, Money b) => new(a.Amount - b.Amount);

    public static Money operator *(Money money, int quantity) => new(money.Amount * quantity);

    private static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}

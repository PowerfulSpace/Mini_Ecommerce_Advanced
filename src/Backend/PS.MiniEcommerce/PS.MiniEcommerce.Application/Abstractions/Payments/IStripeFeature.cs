namespace PS.MiniEcommerce.Application.Abstractions.Payments;

public interface IStripeFeature
{
    bool Enabled { get; }
}

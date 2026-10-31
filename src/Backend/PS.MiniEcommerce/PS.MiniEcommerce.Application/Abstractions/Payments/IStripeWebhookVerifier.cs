namespace PS.MiniEcommerce.Application.Abstractions.Payments;

public interface IStripeWebhookVerifier
{
    bool TryVerify(string payload, string signatureHeader);
}

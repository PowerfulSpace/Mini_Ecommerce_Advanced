namespace PS.MiniEcommerce.Application.Abstractions.Payments;

public sealed record PaymentProviderResult(bool Success, string? ProviderPaymentId)
{
    public static PaymentProviderResult Succeeded(string? providerPaymentId = "mock") =>
        new(true, providerPaymentId);

    public static PaymentProviderResult Failed() => new(false, null);
}

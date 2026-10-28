namespace PS.MiniEcommerce.API.Contracts;

public sealed record RefundPaymentRequest(
    decimal Amount,
    string? Reason);

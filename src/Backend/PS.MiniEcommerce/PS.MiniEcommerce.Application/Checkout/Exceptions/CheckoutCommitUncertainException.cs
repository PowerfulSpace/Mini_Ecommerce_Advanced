namespace PS.MiniEcommerce.Application.Checkout.Exceptions;

public class CheckoutCommitUncertainException : Exception
{
    public CheckoutCommitUncertainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

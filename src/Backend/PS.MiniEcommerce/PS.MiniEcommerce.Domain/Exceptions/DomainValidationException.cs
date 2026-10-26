namespace PS.MiniEcommerce.Domain.Exceptions;

public class DomainValidationException : DomainException
{
    public DomainValidationException(string message)
        : base(message)
    {
    }
}

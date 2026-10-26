namespace PS.MiniEcommerce.Domain.Exceptions;

public class DomainConflictException : DomainException
{
    public DomainConflictException(string message, string code = "conflict")
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

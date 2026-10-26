namespace PS.MiniEcommerce.Application.Exceptions;

public class NotFoundException : Exception
{
    public NotFoundException(string message, string code = "not_found")
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}

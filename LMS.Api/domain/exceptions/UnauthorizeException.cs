namespace LMS.Api.domain.exceptions;

public sealed class UnauthorizeException : Exception
{
    public UnauthorizeException(string message) : base(message){}
}
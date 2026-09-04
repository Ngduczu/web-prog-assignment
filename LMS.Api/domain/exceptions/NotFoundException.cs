namespace LMS.Api.domain.exceptions;

public sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message){}
}
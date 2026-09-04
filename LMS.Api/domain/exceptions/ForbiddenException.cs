namespace LMS.Api.domain.exceptions;

public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message){}
}
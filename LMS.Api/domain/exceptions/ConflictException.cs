namespace LMS.Api.domain.exceptions;

public sealed class ConflictException : Exception
{
    public ConflictException(string message) : base(message){}
}
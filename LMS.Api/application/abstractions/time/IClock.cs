namespace LMS.Api.application.abstractions.time;

public interface IClock
{
    DateTime UtcNow { get; }
}

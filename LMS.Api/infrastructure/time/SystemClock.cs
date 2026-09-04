using LMS.Api.application.abstractions.time;

namespace LMS.Api.infrastructure.time;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

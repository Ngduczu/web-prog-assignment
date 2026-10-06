using LMS.Api.domain.Entities.Storage;

namespace LMS.Domain.Tests.Storage;

public sealed class FileCleanupTaskTests
{
    [Fact]
    public void FailureIsRetainedForADeferredRetry()
    {
        var createdAt = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var cleanup = new FileCleanupTask("courses/orphan.pdf", createdAt);
        var retryAt = createdAt.AddMinutes(1);

        cleanup.RecordFailure("Storage unavailable", retryAt);

        Assert.Equal(1, cleanup.AttemptCount);
        Assert.Equal("Storage unavailable", cleanup.LastError);
        Assert.Equal(retryAt, cleanup.NextAttemptAt);
    }
}

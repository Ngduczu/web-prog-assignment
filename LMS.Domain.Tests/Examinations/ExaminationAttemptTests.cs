using LMS.Api.domain.Entities.Examinations;
using LMS.Api.domain.Enums.Examinations;

namespace LMS.Domain.Tests.Examinations;

public class ExaminationAttemptTests
{
    [Fact]
    public void DisconnectAndReconnectPreserveActiveAttempt()
    {
        var startedAt = DateTime.UtcNow;
        var attempt = new ExaminationAttempt(Guid.NewGuid(), Guid.NewGuid(), startedAt, 20);

        attempt.Disconnect(startedAt.AddMinutes(5), 900);

        Assert.Equal(ExaminationAttemptStatus.Disconnected, attempt.Status);
        Assert.Equal(900, attempt.RemainingSeconds);
        Assert.NotNull(attempt.DisconnectedAt);

        attempt.Reconnect(startedAt.AddMinutes(6));

        Assert.Equal(ExaminationAttemptStatus.InProgress, attempt.Status);
        Assert.Null(attempt.DisconnectedAt);
    }

    [Fact]
    public void SubmittedAttemptCannotBeSubmittedAgain()
    {
        var attempt = new ExaminationAttempt(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, 20);
        attempt.Submit();

        Assert.Equal(ExaminationAttemptStatus.Submitted, attempt.Status);
        Assert.Equal(ExaminationSubmitReason.StudentSubmitted, attempt.SubmitReason);
        Assert.Throws<InvalidOperationException>(() => attempt.Submit());
    }
}

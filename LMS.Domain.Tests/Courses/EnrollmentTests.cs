using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Enums.Courses;

namespace LMS.Domain.Tests.Courses;

public class EnrollmentTests
{
    [Theory]
    [InlineData(EnrollmentStatus.Accepted)]
    [InlineData(EnrollmentStatus.Rejected)]
    [InlineData(EnrollmentStatus.Cancelled)]
    public void WaitingEnrollmentCanReachTerminalStatus(EnrollmentStatus expectedStatus)
    {
        var enrollment = new Enrollment(Guid.NewGuid(), Guid.NewGuid());

        switch (expectedStatus)
        {
            case EnrollmentStatus.Accepted:
                enrollment.Accept();
                break;
            case EnrollmentStatus.Rejected:
                enrollment.Reject();
                break;
            case EnrollmentStatus.Cancelled:
                enrollment.Cancel();
                break;
        }

        Assert.Equal(expectedStatus, enrollment.Status);
        Assert.True(enrollment.ModifiedAt >= enrollment.CreatedAt);
    }

    [Fact]
    public void TerminalEnrollmentCannotTransitionAgain()
    {
        var enrollment = new Enrollment(Guid.NewGuid(), Guid.NewGuid());
        enrollment.Accept();

        Assert.Throws<InvalidOperationException>(() => enrollment.Cancel());
    }
}

using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Enums.Courses;

namespace LMS.Domain.Tests.Courses;

public class SubmissionTests
{
    [Fact]
    public void UnsubmitReturnsSubmissionToCleanDraftState()
    {
        var submission = new Submission(Guid.NewGuid(), Guid.NewGuid());

        submission.Submit();
        submission.Unsubmit();

        Assert.Equal(SubmissionStatus.Draft, submission.Status);
        Assert.Null(submission.SubmittedAt);
    }
}

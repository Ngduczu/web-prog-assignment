using LMS.Api.domain.Enums;

namespace LMS.Api.domain.Entities;

public class Submission
{
    public Guid Id { get; private set; }

    public Guid ExerciseId { get; private set; }
    public Exercise? Exercise { get; private set; }

    public Guid StudentId { get; private set; }
    public Users? Student { get; private set; }

    public SubmissionStatus Status { get; private set; }
    public SubmissionAttachment? Attachment { get; private set; }

    public DateTime? SubmittedAt { get; private set; }

    public DateTime CreatedOn { get; private set; }
    public DateTime ModifiedOn { get; private set; }

    private Submission() { }

    public Submission(
        Guid exerciseId,
        Guid studentId)
    {
        Id = Guid.NewGuid();

        ExerciseId = exerciseId;
        StudentId = studentId;

        Status = SubmissionStatus.Draft;

        CreatedOn = DateTime.UtcNow;
        ModifiedOn = DateTime.UtcNow;
    }

    private void Touch()
    {
        ModifiedOn = DateTime.UtcNow;
    }

    public void Submit()
    {
        if (Status != SubmissionStatus.Draft)
            throw new InvalidOperationException("Only draft submissions can be submitted.");
        Status = SubmissionStatus.Submitted;
        SubmittedAt = DateTime.UtcNow;

        Touch();
    }

    public void Unsubmit()
    {
        if (Status != SubmissionStatus.Submitted)
            throw new InvalidOperationException("Only submitted submissions can be unsubmitted.");
        Status = SubmissionStatus.Draft;

        Touch();
    }

}
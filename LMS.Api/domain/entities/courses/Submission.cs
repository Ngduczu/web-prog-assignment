using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Courses;

namespace LMS.Api.domain.Entities.Courses;

public class Submission
{
    public Guid Id { get; private set; }

    public Guid ExerciseId { get; private set; }
    public Exercise? Exercise { get; private set; }

    public Guid StudentId { get; private set; }
    public User? Student { get; private set; }

    public SubmissionStatus Status { get; private set; }
    public SubmissionAttachment? Attachment { get; private set; }

    public DateTime? SubmittedAt { get; private set; }
    public decimal? Grade { get; private set; }
    public string? Feedback { get; private set; }
    public DateTime? GradedAt { get; private set; }
    public Guid? GradedById { get; private set; }
    public User? GradedBy { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime ModifiedAt { get; private set; }

    private Submission() { }

    public Submission(
        Guid exerciseId,
        Guid studentId)
    {
        Id = Guid.NewGuid();

        ExerciseId = exerciseId;
        StudentId = studentId;

        Status = SubmissionStatus.Draft;

        CreatedAt = DateTime.UtcNow;
        ModifiedAt = DateTime.UtcNow;
    }

    private void Touch()
    {
        ModifiedAt = DateTime.UtcNow;
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
        SubmittedAt = null;

        Touch();
    }

    public void GradeSubmission(decimal grade, string? feedback, Guid gradedById, DateTime gradedAt)
    {
        if (Status is not SubmissionStatus.Submitted and not SubmissionStatus.Graded)
            throw new InvalidOperationException("Only submitted work can be graded.");

        Grade = grade;
        Feedback = feedback;
        GradedById = gradedById;
        GradedAt = gradedAt;
        Status = SubmissionStatus.Graded;
        ModifiedAt = gradedAt;
    }

    public void RecordAttachmentChange(DateTime modifiedAt)
    {
        if (Status != SubmissionStatus.Draft)
            throw new InvalidOperationException("Only draft work can have its file replaced.");

        ModifiedAt = modifiedAt;
    }

    public void AddAttachment(SubmissionAttachment attachment, DateTime modifiedAt)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        if (attachment.SubmissionId != Id)
            throw new InvalidOperationException("The attachment does not belong to this submission.");
        if (Attachment is not null)
            throw new InvalidOperationException("The submission already has an attachment.");

        RecordAttachmentChange(modifiedAt);
        Attachment = attachment;
    }

}

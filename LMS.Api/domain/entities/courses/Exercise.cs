using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Courses;

namespace LMS.Api.domain.Entities.Courses;

public class Exercise
{
    public Guid Id { get; private set; }

    public Guid CourseId { get; private set; }
    public Course? Course { get; private set; }

    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }

    public DateTime? DueAt { get; private set; }
    public ExerciseStatus Status { get; private set; }
    public ExerciseAttachment? Attachment { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime ModifiedAt { get; private set; }

    // audit
    public Guid CreatedById { get; private set; }
    public Guid ModifiedById { get; private set; }

    public User? CreatedBy { get; private set; }
    public User? ModifiedBy { get; private set; }

    private Exercise() { }

    public Exercise(
        Guid courseId,
        string title,
        string? description,
        DateTime? dueAt,
        Guid createdById,
        ExerciseStatus status = ExerciseStatus.Draft)
    {
        Id = Guid.NewGuid();

        CourseId = courseId;
        Title = title;
        Description = description;
        DueAt = dueAt;
        Status = status;

        CreatedAt = DateTime.UtcNow;
        ModifiedAt = DateTime.UtcNow;

        CreatedById = createdById;
        ModifiedById = createdById;
    }

    private void Touch()
    {
        ModifiedAt = DateTime.UtcNow;
    }

    public void SetTitle(string title)
    {
        Title = title;
        Touch();
    }

    public void SetDescription(string? description)
    {
        Description = description;
        Touch();
    }

    public void SetDueAt(DateTime? dueAt)
    {
        DueAt = dueAt;
        Touch();
    }

    public void SetStatus(ExerciseStatus status)
    {
        Status = status;
        Touch();
    }

    public void SetModifiedBy(Guid modifiedById)
    {
        ModifiedById = modifiedById;
    }
}

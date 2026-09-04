namespace LMS.Api.domain.Entities;

public class Exercise
{
    public Guid Id { get; private set; }

    public Guid CourseId { get; private set; }
    public Course? Course { get; private set; }

    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }

    public DateTime? DueAt { get; private set; }
    public ExerciseAttachment? Attachment { get; private set; }

    public DateTime CreatedOn { get; private set; }
    public DateTime ModifiedOn { get; private set; }

    // audit
    public Guid CreatedById { get; private set; }
    public Guid ModifiedById { get; private set; }

    public Users? CreatedBy { get; private set; }
    public Users? ModifiedBy { get; private set; }

    private Exercise() { }

    public Exercise(
        Guid courseId,
        string title,
        string? description,
        DateTime? dueAt,
        Guid createdById)
    {
        Id = Guid.NewGuid();

        CourseId = courseId;
        Title = title;
        Description = description;
        DueAt = dueAt;

        CreatedOn = DateTime.UtcNow;
        ModifiedOn = DateTime.UtcNow;

        CreatedById = createdById;
        ModifiedById = createdById;
    }

    private void Touch()
    {
        ModifiedOn = DateTime.UtcNow;
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

    public void SetModifiedBy(Guid modifiedById)
    {
        ModifiedById = modifiedById;
    }
}
using LMS.Api.domain.Enums;

namespace LMS.Api.domain.Entities;

public class Lesson
{
    public Guid Id { get; private set; }

    public Guid CourseId { get; private set; }
    public Course? Course { get; private set; }

    public string Name { get; private set; } = null!;
    public string? Content { get; private set; }

    public int Order { get; private set; }

    public LessonStatus Status { get; private set; }
    public ICollection<Material> Materials { get; private set; } = new List<Material>();

    public DateTime CreatedAt { get; private set; }
    public DateTime ModifiedAt { get; private set; }

    // audit
    public Guid CreatedById { get; private set; }
    public Guid ModifiedById { get; private set; }

    public User? CreatedBy { get; private set; }
    public User? ModifiedBy { get; private set; }

    private Lesson() { }

    public Lesson(
        Guid courseId,
        string name,
        string? content,
        int order,
        Guid createdById,
        LessonStatus status = LessonStatus.Draft)
    {
        Id = Guid.NewGuid();

        CourseId = courseId;
        Name = name;
        Content = content;
        Order = order;
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

    public void SetName(string name)
    {
        Name = name;
        Touch();
    }

    public void SetContent(string? content)
    {
        Content = content;
        Touch();
    }

    public void SetOrder(int order)
    {
        Order = order;
        Touch();
    }

    public void SetStatus(LessonStatus status)
    {
        Status = status;
        Touch();
    }

    public void SetModifiedBy(Guid modifiedById)
    {
        ModifiedById = modifiedById;
    }
}

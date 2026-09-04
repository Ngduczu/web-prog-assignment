using LMS.Api.domain.Enums;
using LMS.Api.domain.exceptions;

namespace LMS.Api.domain.Entities;

public class Course
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public Guid TeacherId { get; private set; }
    public User? Teacher { get; private set; }
    public int MaxStudents { get; private set; }
    public CourseStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime ModifiedAt { get; private set; }

    // audit
    public Guid CreatedById { get; private set; }
    public Guid ModifiedById { get; private set; }

    public User? CreatedBy { get; private set; }
    public User? ModifiedBy { get; private set; }

    public ICollection<Lesson> Lessons { get; private set; } = new List<Lesson>();
    public ICollection<Exercise> Exercises { get; private set; } = new List<Exercise>();
    public ICollection<Examination> Examinations { get; private set; } = new List<Examination>();

    public DateTime? DeletedAt { get; private set; }
    public Guid? DeletedById { get; private set; }
    public User? DeletedBy { get; private set; }
    public bool IsDeleted => DeletedAt.HasValue;

    private Course() { }

    private void Touch()
    {
        ModifiedAt = DateTime.UtcNow;
    }
    
    public Course(
        string name,
        string? description,
        Guid teacherId,
        int maxStudents,
        CourseStatus status,
        Guid createdById)
    {
        Id = Guid.NewGuid();
        Name = name;
        Description = description;
        TeacherId = teacherId;
        MaxStudents = maxStudents;
        Status = status;
        CreatedAt = DateTime.UtcNow;
        ModifiedAt = DateTime.UtcNow;
        CreatedById = createdById;
        ModifiedById = createdById;
    }
    
    public void SetName(string name)
    {
        Name = name;
        Touch();
    }

    public void SetDescription(string? description)
    {
        Description = description;
        Touch();
    }
    
    public void SetMaxStudents(int maxStudents)
    {
        MaxStudents = maxStudents;
        Touch();
    }

    public void SetStatus(CourseStatus status)
    {
        Status = status;
        Touch();
    }
    
    public void SetModifiedBy(Guid modifiedById)
    {
        ModifiedById = modifiedById;
    }

    public void Delete(Guid deletedById)
    {
        if (IsDeleted)
        {
            throw new ConflictException("This course is already deleted");
        }

        DeletedAt = DateTime.UtcNow;
        DeletedById = deletedById;
        ModifiedById = deletedById;
        Touch();
    }

    public void Restore()
    {
        if (!IsDeleted)
        {
            throw new ConflictException("Only deleted courses can be restored");
        }

        DeletedAt = null;
        DeletedById = null;
        Touch();
    }

}

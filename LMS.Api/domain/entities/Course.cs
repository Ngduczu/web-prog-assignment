using LMS.Api.domain.Enums;
using LMS.Api.domain.exceptions;

namespace LMS.Api.domain.Entities;

public class Course
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public Guid TeacherId { get; private set; }
    public Users? Teacher { get; private set; }
    public int MaxStudents { get; private set; }
    public CourseStatus Status { get; private set; }
    public DateTime CreatedOn { get; private set; }
    public DateTime ModifiedOn { get; private set; }

    // audit
    public Guid CreatedById { get; private set; }
    public Guid ModifiedById { get; private set; }

    public Users? CreatedBy { get; private set; }
    public Users? ModifiedBy { get; private set; }

    public ICollection<Lesson> Lessons { get; private set; } = new List<Lesson>();
    public ICollection<Exercise> Exercises { get; private set; } = new List<Exercise>();
    public ICollection<Examination> Examinations { get; private set; } = new List<Examination>();

    public DateTime? DeletedAt { get; private set; }
    public Guid? DeletedById { get; private set; }
    public Users? DeletedBy { get; private set; }

    private Course() { }

    private void Touch()
    {
        ModifiedOn = DateTime.UtcNow;
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
        CreatedOn = DateTime.UtcNow;
        ModifiedOn = DateTime.UtcNow;
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
        if (Status == CourseStatus.Deleted)
        {
            throw new ConflictException("This course is already deleted");
        }

        Status = CourseStatus.Deleted;
        DeletedAt = DateTime.UtcNow;
        DeletedById = deletedById;
        ModifiedById = deletedById;
        Touch();
    }

    public void Restore()
    {
        if (Status != CourseStatus.Deleted)
        {
            throw new ConflictException("Only deleted courses can be restored");
        }

        Status = CourseStatus.Closed;
        DeletedAt = null;
        DeletedById = null;
        Touch();
    }

}
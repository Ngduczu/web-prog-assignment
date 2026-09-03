using LMS.Api.domain.Enums;

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
}
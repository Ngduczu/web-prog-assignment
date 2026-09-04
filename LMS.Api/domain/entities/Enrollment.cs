using LMS.Api.domain.Enums;

namespace LMS.Api.domain.Entities;

public class Enrollment
{
    public Guid Id { get; private set; }
    
    
    public Guid StudentId { get; private set; }
    public Guid CourseId { get; private set; }
    public User? Student { get; private set; }
    public Course? Course { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime ModifiedAt { get; private set; }
    
    public EnrollmentStatus Status { get; private set; }

    private Enrollment() { }

    private void Touch()
    {
        ModifiedAt = DateTime.UtcNow;
    }

    public Enrollment(
        Guid studentId,
        Guid courseId,
        EnrollmentStatus status = EnrollmentStatus.Waiting 
    )
    {
        Id = Guid.NewGuid();
        StudentId = studentId;
        CourseId = courseId;
        Status = status;
        CreatedAt = DateTime.UtcNow;
        ModifiedAt = DateTime.UtcNow;
    }

    public void Accept()
    {
        if (Status != EnrollmentStatus.Waiting)
            throw new InvalidOperationException(
                "Only waiting enrollment can be accepted.");
        Status = EnrollmentStatus.Accepted;
        Touch();
    }

    public void Reject()
    {
        if (Status != EnrollmentStatus.Waiting)
            throw new InvalidOperationException(
                "Only waiting enrollments can be rejected.");
        Status = EnrollmentStatus.Rejected;
        Touch();
    }

    public void Cancel()
    {
        if (Status != EnrollmentStatus.Waiting)
            throw new InvalidOperationException(
                "Only waiting enrollment can be cancelled.");
        Status = EnrollmentStatus.Cancelled;
        Touch();
    }
}

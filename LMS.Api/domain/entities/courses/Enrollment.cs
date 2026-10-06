using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Courses;
using LMS.Api.domain.exceptions;

namespace LMS.Api.domain.Entities.Courses;

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
            throw new ConflictException(
                "Only waiting enrollment can be accepted.");
        Status = EnrollmentStatus.Accepted;
        Touch();
    }

    public void Reject()
    {
        if (Status != EnrollmentStatus.Waiting)
            throw new ConflictException(
                "Only waiting enrollments can be rejected.");
        Status = EnrollmentStatus.Rejected;
        Touch();
    }

    public void Cancel()
    {
        if (Status is not EnrollmentStatus.Waiting and not EnrollmentStatus.Accepted)
            throw new ConflictException(
                "Only waiting or accepted enrollment can be cancelled.");
        Status = EnrollmentStatus.Cancelled;
        Touch();
    }

    public void RequestAgain()
    {
        if (Status is not EnrollmentStatus.Rejected and not EnrollmentStatus.Cancelled)
            throw new ConflictException("Only rejected or cancelled enrollment can be requested again.");
        Status = EnrollmentStatus.Waiting;
        Touch();
    }
}

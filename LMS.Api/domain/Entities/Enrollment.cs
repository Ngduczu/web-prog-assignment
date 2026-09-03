using LMS.Api.domain.Enums;

namespace LMS.Api.domain.Entities;

public class Enrollment
{
    public Guid  EnrollmentId { get; private set; }
    
    
    public Guid StudentId { get; private set; }
    public Guid CourseId { get; private set; }
    public Users? Student { get; private set; }
    public Course? Course { get; private set; }

    public DateTime CreatedOn  { get; private set; }
    public DateTime ModifiedOn  { get; private set; }
    
    public EnrollmentStatus Status { get; private set; }

    private Enrollment() { }

    private void Touch()
    {
        ModifiedOn = DateTime.UtcNow;
    }

    public Enrollment(
        Guid studentId,
        Guid courseId,
        EnrollmentStatus status = EnrollmentStatus.Waiting 
    )
    {
        EnrollmentId = Guid.NewGuid();
        StudentId = studentId;
        CourseId = courseId;
        Status = status;
        CreatedOn = DateTime.UtcNow;
        ModifiedOn = DateTime.UtcNow;
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
}
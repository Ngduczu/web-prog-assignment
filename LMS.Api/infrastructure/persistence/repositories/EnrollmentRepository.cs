using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Enums.Courses;
using LMS.Api.domain.repositories;
using Microsoft.EntityFrameworkCore;

namespace LMS.Api.infrastructure.Persistence.Repositories;

public sealed class EnrollmentRepository : IEnrollmentRepository
{
    private readonly AppDbContext _dbContext;

    public EnrollmentRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Enrollment?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.Enrollments.SingleOrDefaultAsync(enrollment => enrollment.Id == id, cancellationToken);

    public Task<Enrollment?> GetByStudentAndCourseAsync(
        Guid studentId,
        Guid courseId,
        CancellationToken cancellationToken) =>
        _dbContext.Enrollments.SingleOrDefaultAsync(
            enrollment => enrollment.StudentId == studentId && enrollment.CourseId == courseId,
            cancellationToken);

    public async Task<IReadOnlyList<Enrollment>> ListByStudentAsync(
        Guid studentId,
        CancellationToken cancellationToken) =>
        await _dbContext.Enrollments
            .AsNoTracking()
            .Where(enrollment => enrollment.StudentId == studentId)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Enrollment>> ListByCourseAsync(
        Guid courseId,
        int skip,
        int take,
        CancellationToken cancellationToken) =>
        await _dbContext.Enrollments
            .AsNoTracking()
            .Where(enrollment => enrollment.CourseId == courseId)
            .OrderByDescending(enrollment => enrollment.CreatedAt)
            .ThenBy(enrollment => enrollment.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountByCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
        _dbContext.Enrollments.CountAsync(
            enrollment => enrollment.CourseId == courseId,
            cancellationToken);

    public Task<int> CountAcceptedByCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
        _dbContext.Enrollments.CountAsync(
            enrollment => enrollment.CourseId == courseId && enrollment.Status == EnrollmentStatus.Accepted,
            cancellationToken);

    public void Add(Enrollment enrollment)
    {
        _dbContext.Enrollments.Add(enrollment);
    }
}

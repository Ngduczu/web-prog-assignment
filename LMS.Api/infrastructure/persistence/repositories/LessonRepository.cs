using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.repositories;
using Microsoft.EntityFrameworkCore;

namespace LMS.Api.infrastructure.Persistence.Repositories;

public sealed class LessonRepository : ILessonRepository
{
    private readonly AppDbContext _dbContext;

    public LessonRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Lesson?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.Lessons.SingleOrDefaultAsync(lesson => lesson.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Lesson>> ListByCourseAsync(
        Guid courseId,
        CancellationToken cancellationToken) =>
        await _dbContext.Lessons
            .AsNoTracking()
            .Where(lesson => lesson.CourseId == courseId)
            .OrderBy(lesson => lesson.Order)
            .ThenBy(lesson => lesson.Id)
            .ToListAsync(cancellationToken);

    public Task<bool> OrderExistsAsync(
        Guid courseId,
        int order,
        Guid? excludingLessonId,
        CancellationToken cancellationToken) =>
        _dbContext.Lessons.AnyAsync(
            lesson => lesson.CourseId == courseId &&
                      lesson.Order == order &&
                      (excludingLessonId == null || lesson.Id != excludingLessonId),
            cancellationToken);

    public void Add(Lesson lesson)
    {
        _dbContext.Lessons.Add(lesson);
    }

    public void Remove(Lesson lesson) => _dbContext.Lessons.Remove(lesson);
}

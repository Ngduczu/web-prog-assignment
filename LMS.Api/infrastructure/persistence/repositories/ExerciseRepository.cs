using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.repositories;
using Microsoft.EntityFrameworkCore;

namespace LMS.Api.infrastructure.Persistence.Repositories;

public sealed class ExerciseRepository : IExerciseRepository
{
    private readonly AppDbContext _dbContext;

    public ExerciseRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Exercise?> GetByIdAsync(
        Guid id,
        bool includeAttachment,
        CancellationToken cancellationToken)
    {
        IQueryable<Exercise> query = _dbContext.Exercises;
        if (includeAttachment)
        {
            query = query.Include(exercise => exercise.Attachment);
        }

        return query.SingleOrDefaultAsync(exercise => exercise.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Exercise>> ListByCourseAsync(
        Guid courseId,
        CancellationToken cancellationToken) =>
        await _dbContext.Exercises
            .AsNoTracking()
            .Include(exercise => exercise.Attachment)
            .Where(exercise => exercise.CourseId == courseId)
            .OrderBy(exercise => exercise.DueAt)
            .ThenBy(exercise => exercise.Id)
            .ToListAsync(cancellationToken);

    public void Add(Exercise exercise)
    {
        _dbContext.Exercises.Add(exercise);
    }

    public void AddAttachment(ExerciseAttachment attachment)
    {
        _dbContext.ExerciseAttachments.Add(attachment);
    }

    public void Remove(Exercise exercise) => _dbContext.Exercises.Remove(exercise);
}

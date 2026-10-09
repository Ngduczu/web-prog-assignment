using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Enums.Courses;
using LMS.Api.domain.repositories;
using Microsoft.EntityFrameworkCore;

namespace LMS.Api.infrastructure.Persistence.Repositories;

public sealed class SubmissionRepository : ISubmissionRepository
{
    private readonly AppDbContext _dbContext;

    public SubmissionRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Submission?> GetByIdAsync(
        Guid id,
        bool includeAttachment,
        CancellationToken cancellationToken)
    {
        IQueryable<Submission> query = _dbContext.Submissions;
        if (includeAttachment)
        {
            query = query.Include(submission => submission.Attachment);
        }

        return query.SingleOrDefaultAsync(submission => submission.Id == id, cancellationToken);
    }

    public Task<Submission?> GetByExerciseAndStudentAsync(
        Guid exerciseId,
        Guid studentId,
        bool includeAttachment,
        CancellationToken cancellationToken)
    {
        IQueryable<Submission> query = _dbContext.Submissions;
        if (includeAttachment)
        {
            query = query.Include(submission => submission.Attachment);
        }

        return query.SingleOrDefaultAsync(
            submission => submission.ExerciseId == exerciseId && submission.StudentId == studentId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Submission>> ListSubmittedByExerciseAsync(
        Guid exerciseId,
        int skip,
        int take,
        CancellationToken cancellationToken) =>
        await _dbContext.Submissions
            .AsNoTracking()
            .Include(submission => submission.Attachment)
            .Include(submission => submission.Student)
            .Where(submission =>
                submission.ExerciseId == exerciseId &&
                submission.Status != SubmissionStatus.Draft)
            .OrderByDescending(submission => submission.SubmittedAt)
            .ThenBy(submission => submission.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> CountSubmittedByExerciseAsync(
        Guid exerciseId,
        CancellationToken cancellationToken) =>
        _dbContext.Submissions.CountAsync(
            submission =>
                submission.ExerciseId == exerciseId &&
                submission.Status != SubmissionStatus.Draft,
            cancellationToken);

    public void Add(Submission submission)
    {
        _dbContext.Submissions.Add(submission);
    }

    public void AddAttachment(SubmissionAttachment attachment)
    {
        _dbContext.SubmissionAttachments.Add(attachment);
    }
}

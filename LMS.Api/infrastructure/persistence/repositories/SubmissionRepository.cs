using LMS.Api.domain.Entities.Courses;
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

    public void Add(Submission submission)
    {
        _dbContext.Submissions.Add(submission);
    }

    public void AddAttachment(SubmissionAttachment attachment)
    {
        _dbContext.SubmissionAttachments.Add(attachment);
    }
}

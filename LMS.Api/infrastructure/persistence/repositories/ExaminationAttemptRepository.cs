using LMS.Api.domain.Entities.Examinations;
using LMS.Api.domain.Enums.Examinations;
using LMS.Api.domain.repositories;
using Microsoft.EntityFrameworkCore;

namespace LMS.Api.infrastructure.Persistence.Repositories;

public sealed class ExaminationAttemptRepository : IExaminationAttemptRepository
{
    private readonly AppDbContext _dbContext;

    public ExaminationAttemptRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<ExaminationAttempt?> GetByIdAsync(
        Guid id,
        bool includeAnswers,
        CancellationToken cancellationToken)
    {
        IQueryable<ExaminationAttempt> query = _dbContext.ExaminationAttempts;
        if (includeAnswers)
        {
            query = query
                .Include(attempt => attempt.Answers)
                    .ThenInclude(answer => answer.FillInBlankAnswers)
                .AsSplitQuery();
        }

        return query.SingleOrDefaultAsync(attempt => attempt.Id == id, cancellationToken);
    }

    public Task<ExaminationAttempt?> GetActiveAsync(
        Guid examinationId,
        Guid studentId,
        CancellationToken cancellationToken) =>
        _dbContext.ExaminationAttempts.SingleOrDefaultAsync(
            attempt => attempt.ExaminationId == examinationId &&
                       attempt.StudentId == studentId &&
                       (attempt.Status == ExaminationAttemptStatus.InProgress ||
                        attempt.Status == ExaminationAttemptStatus.Disconnected),
            cancellationToken);

    public Task<StudentAnswer?> GetAnswerAsync(
        Guid attemptId,
        Guid examinationQuestionId,
        bool includeFillInBlankAnswers,
        CancellationToken cancellationToken)
    {
        IQueryable<StudentAnswer> query = _dbContext.StudentAnswers;
        if (includeFillInBlankAnswers)
        {
            query = query.Include(answer => answer.FillInBlankAnswers);
        }

        return query.SingleOrDefaultAsync(
            answer => answer.ExaminationAttemptId == attemptId &&
                      answer.ExaminationQuestionId == examinationQuestionId,
            cancellationToken);
    }

    public void Add(ExaminationAttempt attempt)
    {
        _dbContext.ExaminationAttempts.Add(attempt);
    }

    public void AddAnswer(StudentAnswer answer)
    {
        _dbContext.StudentAnswers.Add(answer);
    }

    public void AddFillInBlankAnswer(StudentFillInBlankAnswer answer)
    {
        _dbContext.StudentFillInBlankAnswers.Add(answer);
    }

    public void RemoveFillInBlankAnswer(StudentFillInBlankAnswer answer)
    {
        _dbContext.StudentFillInBlankAnswers.Remove(answer);
    }

    public void AddViolation(ExaminationViolation violation)
    {
        _dbContext.ExaminationViolations.Add(violation);
    }
}

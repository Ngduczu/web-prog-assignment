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

    public Task<ExaminationAttempt?> GetLatestAsync(
        Guid examinationId,
        Guid studentId,
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

        return query
            .Where(attempt =>
                attempt.ExaminationId == examinationId &&
                attempt.StudentId == studentId)
            .OrderByDescending(attempt =>
                attempt.Status == ExaminationAttemptStatus.InProgress ||
                attempt.Status == ExaminationAttemptStatus.Disconnected)
            .ThenByDescending(attempt => attempt.StartedAt)
            .ThenByDescending(attempt => attempt.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

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

    public async Task<IReadOnlyList<ExaminationAttempt>> ListActiveByExaminationAsync(
        Guid examinationId,
        CancellationToken cancellationToken) =>
        await _dbContext.ExaminationAttempts
            .Include(attempt => attempt.Answers)
                .ThenInclude(answer => answer.FillInBlankAnswers)
            .Where(attempt =>
                attempt.ExaminationId == examinationId &&
                (attempt.Status == ExaminationAttemptStatus.InProgress ||
                 attempt.Status == ExaminationAttemptStatus.Disconnected))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ExaminationAttemptOverview>> ListByExaminationAsync(
        Guid examinationId,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var attempts = await _dbContext.ExaminationAttempts
            .Include(attempt => attempt.Student)
            .Where(attempt => attempt.ExaminationId == examinationId)
            .OrderByDescending(attempt => attempt.StartedAt)
            .ThenByDescending(attempt => attempt.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return attempts.Select(attempt =>
            new ExaminationAttemptOverview(
                attempt,
                attempt.Student ?? throw new InvalidOperationException("Attempt student was not loaded.")))
            .ToArray();
    }

    public Task<int> CountByExaminationAsync(
        Guid examinationId,
        CancellationToken cancellationToken) =>
        _dbContext.ExaminationAttempts.CountAsync(
            attempt => attempt.ExaminationId == examinationId,
            cancellationToken);

    public async Task<IReadOnlyList<ExaminationAttempt>> ListExpiredAsync(
        DateTime now,
        int take,
        CancellationToken cancellationToken) =>
        await _dbContext.ExaminationAttempts
            .AsSplitQuery()
            .Include(attempt => attempt.Examination!)
            .Include(attempt => attempt.Examination!)
                .ThenInclude(examination => examination.Questions)
            .Include(attempt => attempt.Examination!)
                .ThenInclude(examination => examination.Questions)
                    .ThenInclude(question => question.FillInBlankAnswerKeys)
            .Include(attempt => attempt.Answers)
                .ThenInclude(answer => answer.FillInBlankAnswers)
            .Where(attempt =>
                (attempt.Status == ExaminationAttemptStatus.InProgress &&
                 (attempt.StartedAt.AddMinutes(attempt.Examination!.DurationMinutes) <= now ||
                  attempt.Examination!.DueAt <= now)) ||
                (attempt.Status == ExaminationAttemptStatus.Disconnected &&
                 attempt.DisconnectedAt != null &&
                 (attempt.DisconnectedAt.Value.AddMinutes(attempt.Examination!.MaxDisconnectMinutes) <= now ||
                  attempt.StartedAt.AddMinutes(attempt.Examination!.DurationMinutes) <= now ||
                  attempt.Examination!.DueAt <= now)))
            .OrderBy(attempt => attempt.LastActivityAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public void Add(ExaminationAttempt attempt)
    {
        _dbContext.ExaminationAttempts.Add(attempt);
    }

    public void Remove(ExaminationAttempt attempt)
    {
        _dbContext.ExaminationAttempts.Remove(attempt);
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

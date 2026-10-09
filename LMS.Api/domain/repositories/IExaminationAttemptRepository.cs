using LMS.Api.domain.Entities.Examinations;
using LMS.Api.domain.Entities.Users;

namespace LMS.Api.domain.repositories;

public sealed record ExaminationAttemptOverview(ExaminationAttempt Attempt, User Student);

public interface IExaminationAttemptRepository
{
    Task<ExaminationAttempt?> GetByIdAsync(
        Guid id,
        bool includeAnswers,
        CancellationToken cancellationToken);
    Task<ExaminationAttempt?> GetActiveAsync(
        Guid examinationId,
        Guid studentId,
        CancellationToken cancellationToken);
    Task<ExaminationAttempt?> GetLatestAsync(
        Guid examinationId,
        Guid studentId,
        bool includeAnswers,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ExaminationAttempt>> ListActiveByExaminationAsync(
        Guid examinationId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ExaminationAttemptOverview>> ListByExaminationAsync(
        Guid examinationId,
        int skip,
        int take,
        CancellationToken cancellationToken);
    Task<int> CountByExaminationAsync(
        Guid examinationId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ExaminationAttempt>> ListExpiredAsync(
        DateTime now,
        int take,
        CancellationToken cancellationToken);
    Task<StudentAnswer?> GetAnswerAsync(
        Guid attemptId,
        Guid examinationQuestionId,
        bool includeFillInBlankAnswers,
        CancellationToken cancellationToken);
    void Add(ExaminationAttempt attempt);
    void AddAnswer(StudentAnswer answer);
    void AddFillInBlankAnswer(StudentFillInBlankAnswer answer);
    void RemoveFillInBlankAnswer(StudentFillInBlankAnswer answer);
    void AddViolation(ExaminationViolation violation);
}

using LMS.Api.domain.Entities.Questions;

namespace LMS.Api.domain.repositories;

public interface IQuestionBankRepository
{
    Task<QuestionBank?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<QuestionBank>> ListByIdsAsync(
        IReadOnlyCollection<Guid> questionBankIds,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<QuestionBank>> ListByTeacherAsync(Guid teacherId, CancellationToken cancellationToken);
    Task<bool> IsReferencedByExaminationAsync(Guid questionBankId, CancellationToken cancellationToken) =>
        Task.FromResult(false);
    void Add(QuestionBank questionBank);
    void Remove(QuestionBank questionBank) => throw new NotSupportedException();
}

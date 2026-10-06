using LMS.Api.domain.Entities.Questions;

namespace LMS.Api.domain.repositories;

public interface IQuestionRepository
{
    Task<Question?> GetByIdAsync(Guid id, bool includeAnswers, CancellationToken cancellationToken);
    Task<IReadOnlyList<Question>> ListByQuestionBankAsync(Guid questionBankId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Question>> ListByQuestionBankIdsAsync(
        IReadOnlyCollection<Guid> questionBankIds,
        CancellationToken cancellationToken);
    void Add(Question question);
    void Remove(Question question) => throw new NotSupportedException();
}

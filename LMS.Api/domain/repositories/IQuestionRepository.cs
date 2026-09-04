using LMS.Api.domain.Entities.Questions;

namespace LMS.Api.domain.repositories;

public interface IQuestionRepository
{
    Task<Question?> GetByIdAsync(Guid id, bool includeAnswers, CancellationToken cancellationToken);
    Task<IReadOnlyList<Question>> ListByQuestionBankAsync(Guid questionBankId, CancellationToken cancellationToken);
    void Add(Question question);
}

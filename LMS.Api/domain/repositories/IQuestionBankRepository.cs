using LMS.Api.domain.Entities.Questions;

namespace LMS.Api.domain.repositories;

public interface IQuestionBankRepository
{
    Task<QuestionBank?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<QuestionBank>> ListByTeacherAsync(Guid teacherId, CancellationToken cancellationToken);
    void Add(QuestionBank questionBank);
}

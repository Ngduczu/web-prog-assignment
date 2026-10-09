using LMS.Api.domain.Entities.Examinations;

namespace LMS.Api.domain.repositories;

public interface IExaminationRepository
{
    Task<Examination?> GetByIdAsync(
        Guid id,
        bool includeDefinition,
        CancellationToken cancellationToken);
    Task<Examination?> GetByIdForUpdateAsync(
        Guid id,
        bool includeDefinition,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<Examination>> ListByCourseAsync(
        Guid courseId,
        CancellationToken cancellationToken);
    void Add(Examination examination);
    void RemoveQuestionDefinition(Examination examination);
}

using LMS.Api.domain.Entities.Examinations;
using LMS.Api.domain.repositories;
using Microsoft.EntityFrameworkCore;

namespace LMS.Api.infrastructure.Persistence.Repositories;

public sealed class ExaminationRepository : IExaminationRepository
{
    private readonly AppDbContext _dbContext;

    public ExaminationRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Examination?> GetByIdAsync(
        Guid id,
        bool includeDefinition,
        CancellationToken cancellationToken)
    {
        IQueryable<Examination> query = _dbContext.Examinations;
        if (includeDefinition)
        {
            query = query
                .Include(examination => examination.QuestionSelectionRules)
                .Include(examination => examination.Questions)
                    .ThenInclude(question => question.MultipleChoiceOptions)
                .Include(examination => examination.Questions)
                    .ThenInclude(question => question.FillInBlankAnswerKeys)
                .AsSplitQuery();
        }

        return query.SingleOrDefaultAsync(examination => examination.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Examination>> ListByCourseAsync(
        Guid courseId,
        CancellationToken cancellationToken) =>
        await _dbContext.Examinations
            .AsNoTracking()
            .Include(examination => examination.QuestionSelectionRules)
            .Include(examination => examination.Questions)
            .Where(examination => examination.CourseId == courseId)
            .OrderByDescending(examination => examination.StartAt)
            .ThenBy(examination => examination.Id)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public void Add(Examination examination)
    {
        _dbContext.Examinations.Add(examination);
    }
}

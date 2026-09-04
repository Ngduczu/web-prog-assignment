using LMS.Api.domain.Entities.Questions;
using LMS.Api.domain.repositories;
using Microsoft.EntityFrameworkCore;

namespace LMS.Api.infrastructure.Persistence.Repositories;

public sealed class QuestionRepository : IQuestionRepository
{
    private readonly AppDbContext _dbContext;

    public QuestionRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Question?> GetByIdAsync(
        Guid id,
        bool includeAnswers,
        CancellationToken cancellationToken)
    {
        IQueryable<Question> query = _dbContext.Questions;
        if (includeAnswers)
        {
            query = query
                .Include(question => question.MultipleChoiceOptions)
                .Include(question => question.FillInBlankAnswerKeys);
        }

        return query.SingleOrDefaultAsync(question => question.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Question>> ListByQuestionBankAsync(
        Guid questionBankId,
        CancellationToken cancellationToken) =>
        await _dbContext.Questions
            .AsNoTracking()
            .Include(question => question.MultipleChoiceOptions)
            .Include(question => question.FillInBlankAnswerKeys)
            .Where(question => question.QuestionBankId == questionBankId)
            .OrderByDescending(question => question.CreatedAt)
            .ThenBy(question => question.Id)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public void Add(Question question)
    {
        _dbContext.Questions.Add(question);
    }
}

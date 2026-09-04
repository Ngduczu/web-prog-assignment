using LMS.Api.domain.Entities.Questions;
using LMS.Api.domain.repositories;
using Microsoft.EntityFrameworkCore;

namespace LMS.Api.infrastructure.Persistence.Repositories;

public sealed class QuestionBankRepository : IQuestionBankRepository
{
    private readonly AppDbContext _dbContext;

    public QuestionBankRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<QuestionBank?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.QuestionBanks.SingleOrDefaultAsync(
            questionBank => questionBank.Id == id,
            cancellationToken);

    public async Task<IReadOnlyList<QuestionBank>> ListByTeacherAsync(
        Guid teacherId,
        CancellationToken cancellationToken) =>
        await _dbContext.QuestionBanks
            .AsNoTracking()
            .Where(questionBank => questionBank.TeacherId == teacherId)
            .OrderBy(questionBank => questionBank.Name)
            .ThenBy(questionBank => questionBank.Id)
            .ToListAsync(cancellationToken);

    public void Add(QuestionBank questionBank)
    {
        _dbContext.QuestionBanks.Add(questionBank);
    }
}

using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.repositories;
using Microsoft.EntityFrameworkCore;

namespace LMS.Api.infrastructure.Persistence.Repositories;

public sealed class MaterialRepository : IMaterialRepository
{
    private readonly AppDbContext _dbContext;

    public MaterialRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Material?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.Materials.SingleOrDefaultAsync(material => material.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Material>> ListByLessonAsync(
        Guid lessonId,
        CancellationToken cancellationToken) =>
        await _dbContext.Materials
            .AsNoTracking()
            .Where(material => material.LessonId == lessonId)
            .OrderBy(material => material.CreatedAt)
            .ThenBy(material => material.Id)
            .ToListAsync(cancellationToken);

    public void Add(Material material)
    {
        _dbContext.Materials.Add(material);
    }

    public void Remove(Material material)
    {
        _dbContext.Materials.Remove(material);
    }
}

using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Enums.Courses;
using LMS.Api.domain.repositories;
using Microsoft.EntityFrameworkCore;

namespace LMS.Api.infrastructure.Persistence.Repositories;

public sealed class CourseRepository : ICourseRepository
{
    private readonly AppDbContext _dbContext;

    public CourseRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Course?> GetByIdAsync(
        Guid id,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var query = includeDeleted
            ? _dbContext.Courses.IgnoreQueryFilters()
            : _dbContext.Courses;
        return query.SingleOrDefaultAsync(course => course.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Course>> ListAsync(
        string? search,
        CourseStatus? status,
        Guid? teacherId,
        bool includeDeleted,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = BuildQuery(search, status, teacherId, includeDeleted);
        return await query
            .AsNoTracking()
            .OrderByDescending(course => course.CreatedAt)
            .ThenBy(course => course.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountAsync(
        string? search,
        CourseStatus? status,
        Guid? teacherId,
        bool includeDeleted,
        CancellationToken cancellationToken) =>
        BuildQuery(search, status, teacherId, includeDeleted).CountAsync(cancellationToken);

    public void Add(Course course)
    {
        _dbContext.Courses.Add(course);
    }

    private IQueryable<Course> BuildQuery(
        string? search,
        CourseStatus? status,
        Guid? teacherId,
        bool includeDeleted)
    {
        IQueryable<Course> query = includeDeleted
            ? _dbContext.Courses.IgnoreQueryFilters()
            : _dbContext.Courses;

        if (search is not null)
        {
            query = query.Where(course =>
                EF.Functions.ILike(course.Name, $"%{search}%") ||
                course.Description != null && EF.Functions.ILike(course.Description, $"%{search}%"));
        }

        if (status is not null)
        {
            query = query.Where(course => course.Status == status);
        }

        if (teacherId is not null)
        {
            query = query.Where(course => course.TeacherId == teacherId);
        }

        return query;
    }
}

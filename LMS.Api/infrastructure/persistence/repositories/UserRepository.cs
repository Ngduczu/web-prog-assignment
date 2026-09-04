using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.repositories;
using Microsoft.EntityFrameworkCore;

namespace LMS.Api.infrastructure.Persistence.Repositories;

public sealed class UserRepository : IUserManagementRepository
{
    private readonly AppDbContext _dbContext;

    public UserRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken);

    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        _dbContext.Users.SingleOrDefaultAsync(user => user.Email == normalizedEmail, cancellationToken);

    public void Add(User user)
    {
        _dbContext.Users.Add(user);
    }

    public async Task<IReadOnlyList<User>> ListAsync(string? search, int skip, int take, CancellationToken cancellationToken)
    {
        var query = Filter(search).OrderBy(user => user.CreatedAt);
        return await query.Skip(skip).Take(take).ToListAsync(cancellationToken);
    }

    public Task<int> CountAsync(string? search, CancellationToken cancellationToken) =>
        Filter(search).CountAsync(cancellationToken);

    private IQueryable<User> Filter(string? search)
    {
        var query = _dbContext.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToLower();
            query = query.Where(user => user.Email.ToLower().Contains(value) || user.FullName.ToLower().Contains(value));
        }

        return query;
    }
}

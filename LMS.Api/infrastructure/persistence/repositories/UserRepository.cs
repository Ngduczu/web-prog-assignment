using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Users;
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

    public async Task<IReadOnlyList<User>> ListByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken) =>
        ids.Count == 0
            ? []
            : await _dbContext.Users
                .AsNoTracking()
                .Where(user => ids.Contains(user.Id))
                .ToListAsync(cancellationToken);

    public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        _dbContext.Users.SingleOrDefaultAsync(user => user.Email == normalizedEmail, cancellationToken);

    public Task<User?> GetByExternalLoginAsync(
        ExternalProvider provider,
        string externalUserId,
        CancellationToken cancellationToken) =>
        _dbContext.ExternalUsers
            .Where(external => external.Provider == provider && external.ExternalUserId == externalUserId)
            .Select(external => external.User)
            .SingleOrDefaultAsync(cancellationToken);

    public Task<ExternalUser?> GetExternalLoginAsync(
        Guid userId,
        ExternalProvider provider,
        CancellationToken cancellationToken) =>
        _dbContext.ExternalUsers.SingleOrDefaultAsync(
            external => external.UserId == userId && external.Provider == provider,
            cancellationToken);

    public void Add(User user)
    {
        _dbContext.Users.Add(user);
    }

    public void AddExternalUser(ExternalUser externalUser)
    {
        _dbContext.ExternalUsers.Add(externalUser);
    }

    public async Task<IReadOnlyList<User>> ListAsync(
        string? search,
        Role? role,
        AccountStatus? status,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var query = Filter(search, role, status).OrderBy(user => user.CreatedAt);
        return await query.Skip(skip).Take(take).ToListAsync(cancellationToken);
    }

    public Task<int> CountAsync(
        string? search,
        Role? role,
        AccountStatus? status,
        CancellationToken cancellationToken) =>
        Filter(search, role, status).CountAsync(cancellationToken);

    public Task<int> CountByRoleAsync(Role role, CancellationToken cancellationToken) =>
        _dbContext.Users.CountAsync(user => user.Role == role, cancellationToken);

    public Task<int> CountActiveByRoleAsync(Role role, CancellationToken cancellationToken) =>
        _dbContext.Users.CountAsync(
            user => user.Role == role && user.Status == AccountStatus.Active,
            cancellationToken);

    public async Task<IReadOnlyList<User>> SearchActiveForChatByNameAsync(
        string normalizedName,
        Guid excludedUserId,
        int take,
        CancellationToken cancellationToken)
    {
        var value = normalizedName.ToLower();
        return await _dbContext.Users
            .AsNoTracking()
            .Where(user =>
                user.Id != excludedUserId &&
                user.Status == AccountStatus.Active &&
                user.FullName.ToLower().Contains(value))
            .OrderByDescending(user => user.FullName.ToLower().StartsWith(value))
            .ThenBy(user => user.FullName)
            .ThenBy(user => user.Email)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    private IQueryable<User> Filter(string? search, Role? role, AccountStatus? status)
    {
        var query = _dbContext.Users.AsQueryable();
        if (role.HasValue)
        {
            query = query.Where(user => user.Role == role.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(user => user.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToLower();
            query = query.Where(user => user.Email.ToLower().Contains(value) || user.FullName.ToLower().Contains(value));
        }

        return query;
    }
}

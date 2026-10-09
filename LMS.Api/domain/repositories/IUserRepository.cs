using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Users;

namespace LMS.Api.domain.repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    async Task<IReadOnlyList<User>> ListByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        var users = new List<User>(ids.Count);
        foreach (var id in ids.Distinct())
        {
            var user = await GetByIdAsync(id, cancellationToken);
            if (user is not null)
            {
                users.Add(user);
            }
        }

        return users;
    }
    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<User?> GetByExternalLoginAsync(ExternalProvider provider, string externalUserId, CancellationToken cancellationToken);
    Task<ExternalUser?> GetExternalLoginAsync(Guid userId, ExternalProvider provider, CancellationToken cancellationToken);
    void Add(User user);
    void AddExternalUser(ExternalUser externalUser);
}

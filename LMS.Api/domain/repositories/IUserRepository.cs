using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Users;

namespace LMS.Api.domain.repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<User?> GetByExternalLoginAsync(ExternalProvider provider, string externalUserId, CancellationToken cancellationToken);
    Task<ExternalUser?> GetExternalLoginAsync(Guid userId, ExternalProvider provider, CancellationToken cancellationToken);
    void Add(User user);
    void AddExternalUser(ExternalUser externalUser);
}

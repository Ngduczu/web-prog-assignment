using LMS.Api.domain.Entities.Users;

namespace LMS.Api.domain.repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
    void Add(User user);
}

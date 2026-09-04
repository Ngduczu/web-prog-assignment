using LMS.Api.domain.Entities.Users;

namespace LMS.Api.domain.repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
}

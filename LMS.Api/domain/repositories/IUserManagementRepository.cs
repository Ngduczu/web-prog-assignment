using LMS.Api.domain.Entities.Users;

namespace LMS.Api.domain.repositories;

public interface IUserManagementRepository : IUserRepository
{
    Task<IReadOnlyList<User>> ListAsync(string? search, int skip, int take, CancellationToken cancellationToken);
    Task<int> CountAsync(string? search, CancellationToken cancellationToken);
}

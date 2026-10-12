using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Users;

namespace LMS.Api.domain.repositories;

public interface IUserManagementRepository : IUserRepository
{
    Task<IReadOnlyList<User>> ListAsync(
        string? search,
        Role? role,
        AccountStatus? status,
        int skip,
        int take,
        CancellationToken cancellationToken);
    Task<int> CountAsync(
        string? search,
        Role? role,
        AccountStatus? status,
        CancellationToken cancellationToken);
    Task<int> CountByRoleAsync(Role role, CancellationToken cancellationToken);
    Task<int> CountActiveByRoleAsync(Role role, CancellationToken cancellationToken);
    Task<IReadOnlyList<User>> SearchActiveForChatByNameAsync(
        string normalizedName,
        Guid excludedUserId,
        int take,
        CancellationToken cancellationToken);
}

using LMS.Api.application.common;
using LMS.Api.domain.Enums.Users;

namespace LMS.Api.application.features.users;

public sealed record UserProfileDto(Guid Id, string Email, string FullName, string? Phone, Role Role, AccountStatus Status, string? AvatarUrl, DateTime CreatedAt, bool HasPassword, bool? IsGoogleLinked = null);
public sealed record UserQuery(
    string? Search = null,
    Role? Role = null,
    AccountStatus? Status = null,
    int Page = 1,
    int PageSize = 20);
public sealed record AdminCreateUserRequest(string Email, string FullName, string? Phone, string Password, Role Role);
public sealed record UpdateUserStatusRequest(AccountStatus Status);
public sealed record AdminUpdateUserRequest(string FullName, string? Phone, Role Role);
public sealed record UpdateMyProfileRequest(string FullName, string? Phone);
public sealed record ChangeMyPasswordRequest(string? CurrentPassword, string NewPassword, string PasswordConfirm);
public sealed record ChatUserSearchDto(Guid Id, string Email, string FullName, string? AvatarUrl, Role Role);
public interface IUserService
{
    Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<PagedResult<UserProfileDto>> ListAsync(UserQuery query, CancellationToken cancellationToken = default);
    Task<UserProfileDto> CreateByAdminAsync(AdminCreateUserRequest request, CancellationToken cancellationToken = default);
    Task<UserProfileDto> UpdateStatusAsync(Guid userId, UpdateUserStatusRequest request, CancellationToken cancellationToken = default);
    Task<UserProfileDto> UpdateByAdminAsync(Guid userId, AdminUpdateUserRequest request, CancellationToken cancellationToken = default);
    Task<UserProfileDto> UpdateMyProfileAsync(UpdateMyProfileRequest request, CancellationToken cancellationToken = default);
    Task ChangeMyPasswordAsync(ChangeMyPasswordRequest request, CancellationToken cancellationToken = default);
    Task<ChatUserSearchDto> FindForChatByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatUserSearchDto>> SearchForChatByNameAsync(string name, CancellationToken cancellationToken = default);
}

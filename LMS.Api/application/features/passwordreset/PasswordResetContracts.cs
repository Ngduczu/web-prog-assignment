using LMS.Api.domain.Enums.Users;

namespace LMS.Api.application.features.passwordreset;

public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Email, string ResetCode, string NewPassword, string PasswordConfirm);
public sealed record PasswordResetRequestedDto(bool Accepted, DateTime CodeExpiresAt);
public sealed record PasswordResetResultDto(Guid UserId, string Email, AccountStatus Status, DateTime ResetAt);
public interface IPasswordResetService
{
    Task<PasswordResetRequestedDto> RequestAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task<PasswordResetResultDto> ResetAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
}

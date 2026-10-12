using LMS.Api.domain.Enums.Users;

namespace LMS.Api.application.features.registration;

public sealed record RegisterAccountRequest(
    string Email,
    string Password,
    string PasswordConfirm,
    string FullName,
    string? Phone);

public sealed record VerifyEmailRequest(string Email, string VerificationCode);
public sealed record ResendVerificationRequest(string Email);

public sealed record RegistrationDto(
    Guid UserId,
    string Email,
    AccountStatus Status,
    DateTime VerificationCodeExpiresAt);
public sealed record EmailVerificationResultDto(Guid UserId, string Email, AccountStatus Status, DateTime VerifiedAt);

public interface IRegistrationService
{
    Task<RegistrationDto> RegisterAsync(
        RegisterAccountRequest request,
        CancellationToken cancellationToken = default);
    Task<EmailVerificationResultDto> VerifyEmailAsync(
        VerifyEmailRequest request,
        CancellationToken cancellationToken = default);
    Task ResendVerificationAsync(
        ResendVerificationRequest request,
        CancellationToken cancellationToken = default);
}

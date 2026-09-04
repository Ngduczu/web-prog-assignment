namespace LMS.Api.application.features.auth;

public sealed record LoginRequest(string Email, string Password);
public sealed record LoginResponse(string AccessToken, DateTime ExpiresAt, string RefreshToken, DateTime RefreshTokenExpiresAt);
public sealed record RefreshTokenRequest(string RefreshToken);
public interface IAuthenticationService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<LoginResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);
}

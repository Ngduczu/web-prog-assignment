namespace LMS.Api.application.features.auth;

public sealed record LoginRequest(string Email, string Password);
public sealed record LoginResponse(string AccessToken, DateTime ExpiresAt, string RefreshToken, DateTime RefreshTokenExpiresAt);
public sealed record RefreshTokenRequest(string RefreshToken);
public sealed record OAuthLoginRequest(string Provider, string Code, string RedirectUri, string CodeVerifier);
public sealed record OAuthLoginCodeRequest(string LoginCode);
public sealed record OAuthLoginCodeResponse(string LoginCode, DateTime ExpiresAt);
public sealed record OAuthLinkRequest(Guid UserId, string Provider, string Code, string RedirectUri, string CodeVerifier);
public sealed record OAuthLinkResponse(Guid UserId, string Provider, string ExternalEmail);
public interface IAuthenticationService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<LoginResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);
    Task<OAuthLoginCodeResponse> LoginWithOAuthAsync(OAuthLoginRequest request, CancellationToken cancellationToken = default);
    Task<LoginResponse> RedeemOAuthLoginCodeAsync(OAuthLoginCodeRequest request, CancellationToken cancellationToken = default);
    Task<OAuthLinkResponse> LinkOAuthAsync(OAuthLinkRequest request, CancellationToken cancellationToken = default);
}

namespace LMS.Api.application.abstractions.auth;

public sealed record OAuthAuthorizationRequest(string RedirectUri, string State, string? CodeChallenge = null);
public sealed record OAuthTokenResult(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt, string TokenType = "Bearer");
public sealed record OAuthUserInfo(string ProviderUserId, string Email, string? FullName, string? AvatarUrl, bool EmailVerified);

public interface IOAuthProvider
{
    string Name { get; }
    string BuildAuthorizationUrl(OAuthAuthorizationRequest request);
    Task<OAuthTokenResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default);
    Task<OAuthUserInfo> GetUserInfoAsync(OAuthTokenResult token, CancellationToken cancellationToken = default);
}

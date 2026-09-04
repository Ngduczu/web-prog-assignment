using LMS.Api.application.abstractions.auth;

namespace LMS.Api.infrastructure.authentication.OAuth;

public class GoogleOAuthProvider : IOAuthProvider
{
    public string Name => "Google";
    public string BuildAuthorizationUrl(OAuthAuthorizationRequest request) => throw new NotImplementedException("Google OAuth flow is scheduled for the final authentication phase.");
    public Task<OAuthTokenResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default) => throw new NotImplementedException("Google OAuth flow is scheduled for the final authentication phase.");
    public Task<OAuthUserInfo> GetUserInfoAsync(OAuthTokenResult token, CancellationToken cancellationToken = default) => throw new NotImplementedException("Google OAuth flow is scheduled for the final authentication phase.");
}

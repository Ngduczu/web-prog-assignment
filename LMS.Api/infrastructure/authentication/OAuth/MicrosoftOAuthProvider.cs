using LMS.Api.application.abstractions.auth;
namespace LMS.Api.infrastructure.authentication.OAuth;

public class MicrosoftOAuthProvider : IOAuthProvider
{
    public string Name => "Microsoft";
    public string BuildAuthorizationUrl(OAuthAuthorizationRequest request) => throw new NotImplementedException("Microsoft OAuth flow is scheduled for the final authentication phase.");
    public Task<OAuthTokenResult> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default) => throw new NotImplementedException("Microsoft OAuth flow is scheduled for the final authentication phase.");
    public Task<OAuthUserInfo> GetUserInfoAsync(OAuthTokenResult token, CancellationToken cancellationToken = default) => throw new NotImplementedException("Microsoft OAuth flow is scheduled for the final authentication phase.");
}

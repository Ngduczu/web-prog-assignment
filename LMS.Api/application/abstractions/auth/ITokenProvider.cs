namespace LMS.Api.application.abstractions.auth;

public interface ITokenProvider
{
    AccessToken CreateAccessToken(Guid userId, string email, string role, long authVersion);
    AccessToken CreateChatRealtimeToken(Guid userId, string email, string role, long authVersion) =>
        throw new NotSupportedException("Chat realtime tokens are not supported by this provider.");
    string CreateRefreshToken();
}

public sealed record AccessToken(string Token, DateTime ExpiresAt);

public static class AuthenticationTokenClaims
{
    public const string TokenUse = "token_use";
    public const string ChatRealtime = "chat_realtime";
}

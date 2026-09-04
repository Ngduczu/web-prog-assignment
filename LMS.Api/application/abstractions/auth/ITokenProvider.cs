namespace LMS.Api.application.abstractions.auth;

public interface ITokenProvider
{
    AccessToken CreateAccessToken(Guid userId, string email, string role, long authVersion);
    string CreateRefreshToken();
}

public sealed record AccessToken(string Token, DateTime ExpiresAt);

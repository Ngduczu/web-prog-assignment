namespace LMS.Api.infrastructure.authentication.OAuth;

public sealed class GoogleOAuthOptions
{
    public const string SectionName = "OAuth:Google";
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = string.Empty;
}
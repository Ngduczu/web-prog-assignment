namespace LMS.Api.infrastructure.authentication.JWT;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "LMS.Api";
    public string Audience { get; set; } = "LMS.Client";
    public string SigningKey { get; set; } = string.Empty;
    public int ExpirationMinutes { get; set; } = 60;
}

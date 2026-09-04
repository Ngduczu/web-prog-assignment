using LMS.Api.application.abstractions.auth;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LMS.Api.infrastructure.authentication.JWT;

public sealed class JwtTokenProvider : ITokenProvider
{
    private readonly JwtOptions _options;
    public JwtTokenProvider(IOptions<JwtOptions> options) => _options = options.Value;
    public AccessToken CreateAccessToken(Guid userId, string email, string role, long authVersion)
    {
        var expires = DateTime.UtcNow.AddMinutes(_options.ExpirationMinutes);
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Email, email), new Claim(ClaimTypes.Role, role), new Claim("auth_version", authVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)), new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")) };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_options.Issuer, _options.Audience, claims, expires: expires, signingCredentials: credentials);
        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
    public string CreateRefreshToken() => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
}

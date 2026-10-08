using LMS.Api.domain.Enums.Users;

namespace LMS.Api.application.features.auth;

internal static class AuthCacheKeys
{
    internal static string RefreshToken(string token) =>
        $"auth:refresh:{AuthSecurity.Sha256Hex(token)}";

    internal static string OAuthLoginCode(string loginCode) =>
        $"auth:oauth-login-code:{AuthSecurity.Sha256Hex(loginCode)}";

    internal static string PendingOAuthIdentity(
        ExternalProvider provider,
        string code,
        string redirectUri,
        string codeVerifier)
    {
        var binding = $"{provider}\n{code.Length}:{code}\n{redirectUri.Length}:{redirectUri}\n{codeVerifier.Length}:{codeVerifier}";
        return $"auth:oauth-identity:{AuthSecurity.Sha256Hex(binding)}";
    }

    internal static string OAuthState(string state) => $"auth:oauth-state:{state}";
    internal static string EmailVerification(Guid userId) =>
        $"auth:email-verification:{userId:N}";
    internal static string EmailVerificationAttempts(Guid userId) =>
        $"auth:email-verification-attempts:{userId:N}";
    internal static string EmailVerificationCooldown(Guid userId) =>
        $"auth:email-verification-cooldown:{userId:N}";
    internal static string PasswordReset(Guid userId) =>
        $"auth:password-reset:{userId:N}";
    internal static string PasswordResetAttempts(Guid userId) =>
        $"auth:password-reset-attempts:{userId:N}";
    internal static string PasswordResetCooldown(Guid userId) =>
        $"auth:password-reset-cooldown:{userId:N}";
}

using LMS.Api.application.abstractions.auth;

namespace LMS.Api.application.features.auth.utils;

internal sealed record RefreshSessionCacheEntry(Guid UserId, long AuthVersion);
internal sealed record OAuthLoginCodeCacheEntry(Guid UserId);
internal sealed record PendingOAuthIdentityCacheEntry(OAuthUserInfo ExternalInfo);
internal sealed record EmailVerificationCacheEntry(Guid UserId, string Email, string CodeHash);
internal sealed record PasswordResetCacheEntry(Guid UserId, string Email, string CodeHash);
internal sealed record OAuthStateCacheEntry(
    string Provider,
    string RedirectUri,
    string CodeVerifier,
    string BrowserBindingHash,
    OAuthFlowPurpose Purpose,
    Guid? UserId);

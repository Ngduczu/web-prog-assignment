using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.caching;
using LMS.Api.application.abstractions.time;
using LMS.Api.infrastructure.authentication.OAuth;
using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Users;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.features.auth.utils;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Api.application.features.auth;

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenProvider _tokens;
    private readonly ICache _cache;
    private readonly IClock _clock;
    private readonly OAuthResolver _oauthResolver;
    private readonly IUnitOfWork _unitOfWork;
    private static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(30);
    private static readonly TimeSpan OAuthLoginCodeLifetime = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan OAuthIdentityRecoveryLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CredentialRecoveryLifetime = TimeSpan.FromMinutes(1);
    public AuthenticationService(IUserRepository users, IPasswordHasher hasher, ITokenProvider tokens, ICache cache, IClock clock, OAuthResolver oauthResolver, IUnitOfWork unitOfWork) { _users = users; _hasher = hasher; _tokens = tokens; _cache = cache; _clock = clock; _oauthResolver = oauthResolver; _unitOfWork = unitOfWork; }
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (!AuthValidation.TryNormalizeEmail(request.Email, out var email) ||
            string.IsNullOrWhiteSpace(request.Password))
            throw new BadRequestException("Email and password are required.");
        var user = await _users.GetByEmailAsync(email, cancellationToken);
        if (user is null || user.PasswordHash is null || !_hasher.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizeException("Invalid email or password.");
        if (user.Status != AccountStatus.Active)
            throw new UnauthorizeException("Account is not active.");
        return await IssueTokensAsync(user, cancellationToken);
    }

    public async Task<LoginResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) throw new BadRequestException("Refresh token is required.");
        var key = AuthCacheKeys.RefreshToken(request.RefreshToken.Trim());
        var session = await _cache.ConsumeAsync<RefreshSessionCacheEntry>(key, cancellationToken) ?? throw new UnauthorizeException("Refresh token is invalid or expired.");
        try
        {
            var user = await _users.GetByIdAsync(session.UserId, cancellationToken) ?? throw new UnauthorizeException("Refresh token is invalid or expired.");
            if (session.AuthVersion != user.AuthVersion) throw new UnauthorizeException("Refresh token is invalid or expired.");
            if (user.Status != AccountStatus.Active) throw new UnauthorizeException("Account is not active.");
            return await IssueTokensAsync(user, cancellationToken);
        }
        catch (Exception exception) when (AuthFailurePolicy.IsCredentialRestorable(exception))
        {
            await TryRestoreCredentialAsync(key, session);
            throw;
        }
    }

    public async Task<OAuthLoginCodeResponse> LoginWithOAuthAsync(
        OAuthLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var (providerType, externalInfo, recoveryKey) = await GetVerifiedExternalIdentityAsync(
            request.Provider,
            request.Code,
            request.RedirectUri,
            request.CodeVerifier,
            cancellationToken);

        var user = await _users.GetByExternalLoginAsync(providerType, externalInfo.ProviderUserId, cancellationToken);
        if (user is null)
        {
            var email = AuthValidation.NormalizeExternalEmail(externalInfo.Email);
            user = await _users.GetByEmailAsync(email, cancellationToken);
            if (user is not null)
            {
                throw new ConflictException(
                    "An account with this email already exists. Sign in to that account and link Google from account settings.");
            }

            user = new User(
                email,
                AuthValidation.NormalizeExternalFullName(externalInfo.FullName, email),
                null,
                null,
                Role.Student,
                AccountStatus.Active);
            _users.Add(user);
            _users.AddExternalUser(new ExternalUser(user.Id, providerType, externalInfo.ProviderUserId));
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        if (user.Status != AccountStatus.Active)
        {
            throw new UnauthorizeException("Account is not active.");
        }

        var result = await CreateOAuthLoginCodeAsync(user.Id, cancellationToken);
        await TryRemovePendingOAuthIdentityAsync(recoveryKey);
        return result;
    }

    public async Task<LoginResponse> RedeemOAuthLoginCodeAsync(
        OAuthLoginCodeRequest request,
        CancellationToken cancellationToken = default)
    {
        var loginCode = request.LoginCode?.Trim();
        if (string.IsNullOrWhiteSpace(loginCode) || loginCode.Length > 128)
        {
            throw new BadRequestException("OAuth login code is required.");
        }

        var loginCodeKey = AuthCacheKeys.OAuthLoginCode(loginCode);
        var entry = await _cache.ConsumeAsync<OAuthLoginCodeCacheEntry>(
            loginCodeKey,
            cancellationToken) ?? throw new UnauthorizeException("OAuth login code is invalid or expired.");
        try
        {
            var user = await _users.GetByIdAsync(entry.UserId, cancellationToken)
                ?? throw new UnauthorizeException("OAuth login code is invalid or expired.");
            if (user.Status != AccountStatus.Active)
            {
                throw new UnauthorizeException("Account is not active.");
            }

            return await IssueTokensAsync(user, cancellationToken);
        }
        catch (Exception exception) when (AuthFailurePolicy.IsCredentialRestorable(exception))
        {
            await TryRestoreCredentialAsync(loginCodeKey, entry);
            throw;
        }
    }

    public async Task<OAuthLinkResponse> LinkOAuthAsync(
        OAuthLinkRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.UserId == Guid.Empty)
        {
            throw new BadRequestException("The account to link is required.");
        }

        var user = await _users.GetByIdAsync(request.UserId, cancellationToken)
            ?? throw new NotFoundException("Account was not found.");
        if (user.Status != AccountStatus.Active)
        {
            throw new UnauthorizeException("Account is not active.");
        }

        var (providerType, externalInfo, recoveryKey) = await GetVerifiedExternalIdentityAsync(
            request.Provider,
            request.Code,
            request.RedirectUri,
            request.CodeVerifier,
            cancellationToken);
        var externalEmail = AuthValidation.NormalizeExternalEmail(externalInfo.Email);
        var owner = await _users.GetByExternalLoginAsync(
            providerType,
            externalInfo.ProviderUserId,
            cancellationToken);
        if (owner is not null)
        {
            throw new ConflictException(owner.Id == user.Id
                ? "This external account is already linked to your account."
                : "This external account is already linked to another account.");
        }

        if (await _users.GetExternalLoginAsync(user.Id, providerType, cancellationToken) is not null)
        {
            throw new ConflictException($"Your account already has a linked {providerType} account.");
        }

        _users.AddExternalUser(new ExternalUser(user.Id, providerType, externalInfo.ProviderUserId));
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        var result = new OAuthLinkResponse(user.Id, providerType.ToString(), externalEmail);
        await TryRemovePendingOAuthIdentityAsync(recoveryKey);
        return result;
    }

    private async Task<(ExternalProvider Provider, OAuthUserInfo ExternalInfo, string RecoveryKey)> GetVerifiedExternalIdentityAsync(
        string providerName,
        string code,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ExternalProvider>(providerName, true, out var providerType) ||
            string.IsNullOrWhiteSpace(code) ||
            string.IsNullOrWhiteSpace(redirectUri) ||
            string.IsNullOrWhiteSpace(codeVerifier))
        {
            throw new BadRequestException("OAuth provider, code, redirect URI and code verifier are required.");
        }

        var recoveryKey = AuthCacheKeys.PendingOAuthIdentity(
            providerType,
            code,
            redirectUri,
            codeVerifier);
        var pendingIdentity = await _cache.GetAsync<PendingOAuthIdentityCacheEntry>(
            recoveryKey,
            cancellationToken);
        if (pendingIdentity is not null)
        {
            return (providerType, pendingIdentity.ExternalInfo, recoveryKey);
        }

        var provider = _oauthResolver.Get(providerType.ToString());
        var token = await provider.ExchangeCodeAsync(code, redirectUri, codeVerifier, cancellationToken);
        var externalInfo = await provider.GetUserInfoAsync(token, cancellationToken);
        if (string.IsNullOrWhiteSpace(externalInfo.ProviderUserId) ||
            externalInfo.ProviderUserId.Length > 255)
        {
            throw new UnauthorizeException("The external account returned an invalid provider identity.");
        }

        if (!externalInfo.EmailVerified)
        {
            throw new UnauthorizeException("The external account email is not verified.");
        }

        await _cache.SetAsync(
            recoveryKey,
            new PendingOAuthIdentityCacheEntry(externalInfo),
            OAuthIdentityRecoveryLifetime,
            cancellationToken);
        return (providerType, externalInfo, recoveryKey);
    }

    private async Task<LoginResponse> IssueTokensAsync(User user, CancellationToken cancellationToken)
    {
        var access = _tokens.CreateAccessToken(
            user.Id,
            user.Email,
            user.Role.ToString(),
            user.AuthVersion);
        var refresh = _tokens.CreateRefreshToken();
        var refreshExpires = _clock.UtcNow.Add(RefreshLifetime);
        await _cache.SetAsync(
            AuthCacheKeys.RefreshToken(refresh),
            new RefreshSessionCacheEntry(user.Id, user.AuthVersion),
            RefreshLifetime,
            cancellationToken);
        return new LoginResponse(access.Token, access.ExpiresAt, refresh, refreshExpires);
    }

    private async Task TryRestoreCredentialAsync<T>(string key, T value)
    {
        try
        {
            await _cache.SetIfNotExistsAsync(
                key,
                value,
                CredentialRecoveryLifetime,
                CancellationToken.None);
        }
        catch
        {
            // Preserve the original downstream failure when recovery storage is unavailable too.
        }
    }

    private async Task TryRemovePendingOAuthIdentityAsync(string key)
    {
        try
        {
            await _cache.RemoveAsync(key, CancellationToken.None);
        }
        catch
        {
            // The state is already consumed; let the short-lived recovery entry expire naturally.
        }
    }

    private async Task<OAuthLoginCodeResponse> CreateOAuthLoginCodeAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var loginCode = AuthSecurity.GenerateUrlToken(32);
            if (await _cache.SetIfNotExistsAsync(
                    AuthCacheKeys.OAuthLoginCode(loginCode),
                    new OAuthLoginCodeCacheEntry(userId),
                    OAuthLoginCodeLifetime,
                    cancellationToken))
            {
                return new OAuthLoginCodeResponse(loginCode, _clock.UtcNow.Add(OAuthLoginCodeLifetime));
            }
        }

        throw new InvalidOperationException("Could not create a unique OAuth login code.");
    }
}

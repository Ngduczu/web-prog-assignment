using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.caching;
using LMS.Api.application.abstractions.time;
using LMS.Api.infrastructure.authentication.OAuth;
using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Users;
using LMS.Api.application.abstractions.persistence;
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
    private static readonly TimeSpan CredentialRecoveryLifetime = TimeSpan.FromMinutes(1);
    public AuthenticationService(IUserRepository users, IPasswordHasher hasher, ITokenProvider tokens, ICache cache, IClock clock, OAuthResolver oauthResolver, IUnitOfWork unitOfWork) { _users = users; _hasher = hasher; _tokens = tokens; _cache = cache; _clock = clock; _oauthResolver = oauthResolver; _unitOfWork = unitOfWork; }
    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !MailAddress.TryCreate(email, out var address) || address.Address != email || string.IsNullOrWhiteSpace(request.Password))
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
        var hash = HashRefreshToken(request.RefreshToken.Trim());
        var key = $"auth:refresh:{hash}";
        var session = await _cache.ConsumeAsync<RefreshSession>(key, cancellationToken) ?? throw new UnauthorizeException("Refresh token is invalid or expired.");
        try
        {
            var user = await _users.GetByIdAsync(session.UserId, cancellationToken) ?? throw new UnauthorizeException("Refresh token is invalid or expired.");
            if (session.AuthVersion != user.AuthVersion) throw new UnauthorizeException("Refresh token is invalid or expired.");
            if (user.Status != AccountStatus.Active) throw new UnauthorizeException("Account is not active.");
            return await IssueTokensAsync(user, cancellationToken);
        }
        catch (Exception exception) when (IsCredentialRestorableFailure(exception))
        {
            await TryRestoreCredentialAsync(key, session);
            throw;
        }
    }

    public async Task<OAuthLoginCodeResponse> LoginWithOAuthAsync(
        OAuthLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        var (providerType, externalInfo) = await GetVerifiedExternalIdentityAsync(
            request.Provider,
            request.Code,
            request.RedirectUri,
            request.CodeVerifier,
            cancellationToken);

        var user = await _users.GetByExternalLoginAsync(providerType, externalInfo.ProviderUserId, cancellationToken);
        if (user is null)
        {
            var email = NormalizeExternalEmail(externalInfo.Email);
            user = await _users.GetByEmailAsync(email, cancellationToken);
            if (user is not null)
            {
                throw new ConflictException(
                    "An account with this email already exists. Sign in to that account and link Google from account settings.");
            }

            user = new User(
                email,
                NormalizeExternalFullName(externalInfo.FullName, email),
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

        return await CreateOAuthLoginCodeAsync(user.Id, cancellationToken);
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

        var entry = await _cache.ConsumeAsync<OAuthLoginCodeEntry>(
            OAuthLoginCodeKey(loginCode),
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
        catch (Exception exception) when (IsCredentialRestorableFailure(exception))
        {
            await TryRestoreCredentialAsync(OAuthLoginCodeKey(loginCode), entry);
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

        var (providerType, externalInfo) = await GetVerifiedExternalIdentityAsync(
            request.Provider,
            request.Code,
            request.RedirectUri,
            request.CodeVerifier,
            cancellationToken);
        var externalEmail = NormalizeExternalEmail(externalInfo.Email);
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
        return new OAuthLinkResponse(user.Id, providerType.ToString(), externalEmail);
    }

    private async Task<(ExternalProvider Provider, OAuthUserInfo ExternalInfo)> GetVerifiedExternalIdentityAsync(
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

        return (providerType, externalInfo);
    }

    private static string NormalizeExternalEmail(string email)
    {
        var normalized = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Length > 255 ||
            !MailAddress.TryCreate(normalized, out var address) ||
            address.Address != normalized)
        {
            throw new UnauthorizeException("The external account returned an invalid email address.");
        }

        return normalized;
    }

    private static string NormalizeExternalFullName(string? fullName, string normalizedEmail)
    {
        var value = string.IsNullOrWhiteSpace(fullName)
            ? normalizedEmail[..normalizedEmail.IndexOf('@')]
            : fullName.Trim();
        return value.Length <= 100 ? value : value[..100];
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
            $"auth:refresh:{HashRefreshToken(refresh)}",
            new RefreshSession(user.Id, user.AuthVersion),
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

    private static bool IsCredentialRestorableFailure(Exception exception) =>
        exception is not BadRequestException
            and not UnauthorizeException
            and not ForbiddenException
            and not NotFoundException
            and not ConflictException;

    private async Task<OAuthLoginCodeResponse> CreateOAuthLoginCodeAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var loginCode = Base64Url(RandomNumberGenerator.GetBytes(32));
            if (await _cache.SetIfNotExistsAsync(
                    OAuthLoginCodeKey(loginCode),
                    new OAuthLoginCodeEntry(userId),
                    OAuthLoginCodeLifetime,
                    cancellationToken))
            {
                return new OAuthLoginCodeResponse(loginCode, _clock.UtcNow.Add(OAuthLoginCodeLifetime));
            }
        }

        throw new InvalidOperationException("Could not create a unique OAuth login code.");
    }

    private sealed record RefreshSession(Guid UserId, long AuthVersion);
    private sealed record OAuthLoginCodeEntry(Guid UserId);

    private static string HashRefreshToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    private static string OAuthLoginCodeKey(string loginCode) =>
        $"auth:oauth-login-code:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(loginCode))).ToLowerInvariant()}";
    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

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
    private static string AuthVersionKey(Guid userId) => $"auth:version:{userId:N}";
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
        return await IssueTokensAsync(user.Id, user.Email, user.Role.ToString(), cancellationToken);
    }

    public async Task<LoginResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) throw new BadRequestException("Refresh token is required.");
        var hash = HashRefreshToken(request.RefreshToken.Trim());
        var session = await _cache.ConsumeAsync<RefreshSession>($"auth:refresh:{hash}", cancellationToken) ?? throw new UnauthorizeException("Refresh token is invalid or expired.");
        var currentVersion = await _cache.GetAsync<long?>($"auth:version:{session.UserId:N}", cancellationToken) ?? 1;
        if (session.AuthVersion != currentVersion) throw new UnauthorizeException("Refresh token is invalid or expired.");
        var user = await _users.GetByIdAsync(session.UserId, cancellationToken) ?? throw new UnauthorizeException("Refresh token is invalid or expired.");
        if (user.Status != AccountStatus.Active) throw new UnauthorizeException("Account is not active.");
        return await IssueTokensAsync(user.Id, user.Email, user.Role.ToString(), cancellationToken);
    }

    public async Task<LoginResponse> LoginWithOAuthAsync(
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
                string.IsNullOrWhiteSpace(externalInfo.FullName) ? email[..email.IndexOf('@')] : externalInfo.FullName.Trim(),
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

        return await IssueTokensAsync(user.Id, user.Email, user.Role.ToString(), cancellationToken);
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

    private async Task<LoginResponse> IssueTokensAsync(Guid userId, string email, string role, CancellationToken cancellationToken)
    {
        var version = await _cache.GetAsync<long?>(AuthVersionKey(userId), cancellationToken) ?? 1;
        await _cache.SetIfNotExistsAsync(AuthVersionKey(userId), version, TimeSpan.FromDays(365), cancellationToken);
        var access = _tokens.CreateAccessToken(userId, email, role, version);
        var refresh = _tokens.CreateRefreshToken();
        var refreshExpires = _clock.UtcNow.Add(RefreshLifetime);
        await _cache.SetAsync($"auth:refresh:{HashRefreshToken(refresh)}", new RefreshSession(userId, version), RefreshLifetime, cancellationToken);
        return new LoginResponse(access.Token, access.ExpiresAt, refresh, refreshExpires);
    }

    private sealed record RefreshSession(Guid UserId, long AuthVersion);

    private static string HashRefreshToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}

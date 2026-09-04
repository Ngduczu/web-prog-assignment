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
        if (!Enum.TryParse<ExternalProvider>(request.Provider, true, out var providerType) ||
            string.IsNullOrWhiteSpace(request.Code) ||
            string.IsNullOrWhiteSpace(request.RedirectUri))
        {
            throw new BadRequestException("OAuth provider, code and redirect URI are required.");
        }

        var provider = _oauthResolver.Get(providerType.ToString());
        var token = await provider.ExchangeCodeAsync(request.Code, request.RedirectUri, request.CodeVerifier, cancellationToken);
        var externalInfo = await provider.GetUserInfoAsync(token, cancellationToken);
        if (!externalInfo.EmailVerified)
        {
            throw new UnauthorizeException("The external account email is not verified.");
        }

        var user = await _users.GetByExternalLoginAsync(providerType, externalInfo.ProviderUserId, cancellationToken);
        if (user is null)
        {
            var email = externalInfo.Email.Trim().ToLowerInvariant();
            user = await _users.GetByEmailAsync(email, cancellationToken);
            if (user is null)
            {
                user = new User(
                    email,
                    string.IsNullOrWhiteSpace(externalInfo.FullName) ? email[..email.IndexOf('@')] : externalInfo.FullName,
                    null,
                    null,
                    Role.Student,
                    AccountStatus.Active);
                _users.Add(user);
            }

            _users.AddExternalUser(new ExternalUser(user.Id, providerType, externalInfo.ProviderUserId));
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        if (user.Status != AccountStatus.Active)
        {
            throw new UnauthorizeException("Account is not active.");
        }

        return await IssueTokensAsync(user.Id, user.Email, user.Role.ToString(), cancellationToken);
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

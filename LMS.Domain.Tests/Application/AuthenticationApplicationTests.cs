using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.caching;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.time;
using LMS.Api.application.features.auth;
using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;
using LMS.Api.infrastructure.authentication.JWT;
using LMS.Api.infrastructure.authentication.OAuth;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace LMS.Domain.Tests.Application;

public sealed class AuthenticationApplicationTests
{
    [Fact]
    public async Task LoginReturnsOnlyTokenMetadataAndUsesHashedPassword()
    {
        var fixture = CreateFixture(AccountStatus.Active);

        var result = await fixture.Service.LoginAsync(new LoginRequest("user@example.com", "password123"));

        Assert.False(string.IsNullOrWhiteSpace(result.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));
        Assert.Equal(TimeSpan.FromDays(30), fixture.Cache.GetExpiry(fixture.Cache.LastRefreshKey));
        Assert.Equal(1L, fixture.Cache.GetValue<long>(fixture.Cache.VersionKey));
    }

    [Fact]
    public async Task RefreshTokenCanOnlyBeUsedOnce()
    {
        var fixture = CreateFixture(AccountStatus.Active);
        var login = await fixture.Service.LoginAsync(new LoginRequest("user@example.com", "password123"));

        var refreshed = await fixture.Service.RefreshAsync(new RefreshTokenRequest(login.RefreshToken));

        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
        await Assert.ThrowsAsync<UnauthorizeException>(() =>
            fixture.Service.RefreshAsync(new RefreshTokenRequest(login.RefreshToken)));
    }

    [Fact]
    public async Task RefreshTokenCanBeRetriedAfterTransientUserLookupFailure()
    {
        var fixture = CreateFixture(AccountStatus.Active);
        var login = await fixture.Service.LoginAsync(new LoginRequest("user@example.com", "password123"));
        fixture.Users.FailNextGetById = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RefreshAsync(new RefreshTokenRequest(login.RefreshToken)));

        var refreshed = await fixture.Service.RefreshAsync(new RefreshTokenRequest(login.RefreshToken));
        Assert.False(string.IsNullOrWhiteSpace(refreshed.AccessToken));
    }

    [Fact]
    public async Task InactiveAccountCannotLogin()
    {
        var fixture = CreateFixture(AccountStatus.Inactive);

        await Assert.ThrowsAsync<UnauthorizeException>(() =>
            fixture.Service.LoginAsync(new LoginRequest("user@example.com", "password123")));
    }

    [Fact]
    public async Task OAuthLoginDoesNotAutomaticallyLinkAnExistingLocalAccount()
    {
        var user = CreateUser("user@example.com");
        var fixture = CreateOAuthFixture(
            user,
            new OAuthUserInfo("google-user", user.Email, "Google User", null, true));

        await Assert.ThrowsAsync<ConflictException>(() => fixture.Service.LoginWithOAuthAsync(
            new OAuthLoginRequest("Google", "code", "https://localhost/callback", "verifier")));

        Assert.Empty(fixture.Users.ExternalUsers);
    }

    [Fact]
    public async Task OAuthLoginCreatesAnAccountWhenTheEmailDoesNotExist()
    {
        var fixture = CreateOAuthFixture(
            [],
            new OAuthUserInfo("google-user", "new-user@example.com", "New User", null, true));

        var result = await fixture.Service.LoginWithOAuthAsync(
            new OAuthLoginRequest("Google", "code", "https://localhost/callback", "verifier"));

        var user = Assert.Single(fixture.Users.Users);
        var externalLogin = Assert.Single(fixture.Users.ExternalUsers);
        Assert.Equal("new-user@example.com", user.Email);
        Assert.Equal(AccountStatus.Active, user.Status);
        Assert.Null(user.PasswordHash);
        Assert.Equal(user.Id, externalLogin.UserId);
        Assert.False(string.IsNullOrWhiteSpace(result.LoginCode));
        Assert.Equal(TimeSpan.FromMinutes(2), fixture.Cache.GetExpiry(fixture.Cache.LastOAuthLoginCodeKey));
    }

    [Fact]
    public async Task OAuthLoginBoundsProviderFullNameToDatabaseColumn()
    {
        var fixture = CreateOAuthFixture(
            [],
            new OAuthUserInfo(
                "google-user",
                "new-user@example.com",
                new string('x', 101),
                null,
                true));

        await fixture.Service.LoginWithOAuthAsync(
            new OAuthLoginRequest("Google", "code", "https://localhost/callback", "verifier"));

        Assert.Equal(100, Assert.Single(fixture.Users.Users).FullName.Length);
    }

    [Fact]
    public async Task OAuthLoginCodeCanOnlyBeRedeemedOnce()
    {
        var fixture = CreateOAuthFixture(
            [],
            new OAuthUserInfo("google-user", "new-user@example.com", "New User", null, true));
        var callback = await fixture.Service.LoginWithOAuthAsync(
            new OAuthLoginRequest("Google", "code", "https://localhost/callback", "verifier"));

        var login = await fixture.Service.RedeemOAuthLoginCodeAsync(
            new OAuthLoginCodeRequest(callback.LoginCode));

        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
        await Assert.ThrowsAsync<UnauthorizeException>(() =>
            fixture.Service.RedeemOAuthLoginCodeAsync(new OAuthLoginCodeRequest(callback.LoginCode)));
    }

    [Fact]
    public async Task OAuthLoginCodeCanBeRetriedAfterTransientUserLookupFailure()
    {
        var fixture = CreateOAuthFixture(
            [],
            new OAuthUserInfo("google-user", "new-user@example.com", "New User", null, true));
        var callback = await fixture.Service.LoginWithOAuthAsync(
            new OAuthLoginRequest("Google", "code", "https://localhost/callback", "verifier"));
        fixture.Users.FailNextGetById = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RedeemOAuthLoginCodeAsync(new OAuthLoginCodeRequest(callback.LoginCode)));

        var login = await fixture.Service.RedeemOAuthLoginCodeAsync(
            new OAuthLoginCodeRequest(callback.LoginCode));
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
    }

    [Fact]
    public async Task ExpiredOAuthLoginCodeIsRejected()
    {
        var fixture = CreateOAuthFixture(
            [],
            new OAuthUserInfo("google-user", "new-user@example.com", "New User", null, true));
        var callback = await fixture.Service.LoginWithOAuthAsync(
            new OAuthLoginRequest("Google", "code", "https://localhost/callback", "verifier"));
        await fixture.Cache.RemoveAsync(fixture.Cache.LastOAuthLoginCodeKey);

        await Assert.ThrowsAsync<UnauthorizeException>(() =>
            fixture.Service.RedeemOAuthLoginCodeAsync(new OAuthLoginCodeRequest(callback.LoginCode)));
    }

    [Fact]
    public async Task ConcurrentOAuthLoginCodeRedemptionHasExactlyOneWinner()
    {
        var fixture = CreateOAuthFixture(
            [],
            new OAuthUserInfo("google-user", "new-user@example.com", "New User", null, true));
        var callback = await fixture.Service.LoginWithOAuthAsync(
            new OAuthLoginRequest("Google", "code", "https://localhost/callback", "verifier"));

        async Task<bool> TryRedeemAsync()
        {
            try
            {
                await fixture.Service.RedeemOAuthLoginCodeAsync(
                    new OAuthLoginCodeRequest(callback.LoginCode));
                return true;
            }
            catch (UnauthorizeException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(TryRedeemAsync(), TryRedeemAsync());

        Assert.Single(results, succeeded => succeeded);
    }

    [Fact]
    public async Task LinkedProviderSubjectCannotBeRedirectedByEmailCollision()
    {
        var owner = CreateUser("owner@example.com");
        var victim = CreateUser("victim@example.com");
        var fixture = CreateOAuthFixture(
            [owner, victim],
            new OAuthUserInfo("google-user", victim.Email, "Google User", null, true));
        fixture.Users.AddExternalUser(new ExternalUser(
            owner.Id,
            ExternalProvider.Google,
            "google-user"));

        var callback = await fixture.Service.LoginWithOAuthAsync(
            new OAuthLoginRequest("Google", "code", "https://localhost/callback", "verifier"));
        var login = await fixture.Service.RedeemOAuthLoginCodeAsync(
            new OAuthLoginCodeRequest(callback.LoginCode));

        Assert.Contains(owner.Id.ToString("N"), login.AccessToken, StringComparison.Ordinal);
        Assert.DoesNotContain(victim.Id.ToString("N"), login.AccessToken, StringComparison.Ordinal);
        Assert.Single(fixture.Users.ExternalUsers);
    }

    [Fact]
    public async Task AuthenticatedAccountCanExplicitlyLinkAnExternalIdentity()
    {
        var user = CreateUser("user@example.com");
        var fixture = CreateOAuthFixture(
            user,
            new OAuthUserInfo("google-user", "different-google@example.com", "Google User", null, true));

        var result = await fixture.Service.LinkOAuthAsync(new OAuthLinkRequest(
            user.Id,
            "Google",
            "code",
            "https://localhost/callback",
            "verifier"));

        var externalLogin = Assert.Single(fixture.Users.ExternalUsers);
        Assert.Equal(user.Id, externalLogin.UserId);
        Assert.Equal(ExternalProvider.Google, externalLogin.Provider);
        Assert.Equal("google-user", externalLogin.ExternalUserId);
        Assert.Equal(user.Id, result.UserId);
        Assert.Equal("different-google@example.com", result.ExternalEmail);
    }

    [Fact]
    public async Task ExternalIdentityCannotBeLinkedToTwoAccounts()
    {
        var firstUser = CreateUser("first@example.com");
        var secondUser = CreateUser("second@example.com");
        var fixture = CreateOAuthFixture(
            [firstUser, secondUser],
            new OAuthUserInfo("google-user", "google@example.com", "Google User", null, true));
        fixture.Users.AddExternalUser(new ExternalUser(
            firstUser.Id,
            ExternalProvider.Google,
            "google-user"));

        await Assert.ThrowsAsync<ConflictException>(() => fixture.Service.LinkOAuthAsync(
            new OAuthLinkRequest(
                secondUser.Id,
                "Google",
                "code",
                "https://localhost/callback",
                "verifier")));
    }

    [Fact]
    public void AccessTokenContainsOnlySelectedAccountClaims()
    {
        var provider = new JwtTokenProvider(Options.Create(new JwtOptions
        {
            Issuer = "LMS.Api",
            Audience = "LMS.Client",
            SigningKey = "integration-test-signing-key-that-is-at-least-32-bytes",
            ExpirationMinutes = 60
        }));

        var userId = Guid.NewGuid();
        var accessToken = provider.CreateAccessToken(userId, "user@example.com", "Student", 7);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(accessToken.Token);

        Assert.Equal(userId.ToString(), token.Claims.Single(claim => claim.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal("user@example.com", token.Claims.Single(claim => claim.Type == ClaimTypes.Email).Value);
        Assert.Equal("Student", token.Claims.Single(claim => claim.Type == ClaimTypes.Role).Value);
        Assert.Equal("7", token.Claims.Single(claim => claim.Type == "auth_version").Value);
        Assert.NotNull(token.Claims.SingleOrDefault(claim => claim.Type == JwtRegisteredClaimNames.Jti));
    }

    private static Fixture CreateFixture(AccountStatus status)
    {
        var user = new User(
            "user@example.com",
            "User",
            null,
            "hash:password123",
            Role.Student,
            status);
        var users = new FakeUserRepository(user);
        var cache = new FakeCache();
        var service = new AuthenticationService(
            users,
            new FakePasswordHasher(),
            new FakeTokenProvider(),
            cache,
            new FakeClock(),
            new OAuthResolver([]),
            new FakeUnitOfWork());
        return new Fixture(service, cache, users);
    }

    private static Fixture CreateOAuthFixture(User user, OAuthUserInfo externalInfo) =>
        CreateOAuthFixture([user], externalInfo);

    private static Fixture CreateOAuthFixture(User[] usersToAdd, OAuthUserInfo externalInfo)
    {
        var users = new FakeUserRepository(usersToAdd);
        var cache = new FakeCache();
        var service = new AuthenticationService(
            users,
            new FakePasswordHasher(),
            new FakeTokenProvider(),
            cache,
            new FakeClock(),
            new OAuthResolver([new FakeOAuthProvider(externalInfo)]),
            new FakeUnitOfWork());
        return new Fixture(service, cache, users);
    }

    private static User CreateUser(string email) => new(
        email,
        "User",
        null,
        "hash:password123",
        Role.Student,
        AccountStatus.Active);

    private sealed record Fixture(
        AuthenticationService Service,
        FakeCache Cache,
        FakeUserRepository Users);

    private sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; } = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string value) => $"hash:{value}";
        public bool Verify(string value, string hash) => hash == $"hash:{value}";
    }

    private sealed class FakeTokenProvider : ITokenProvider
    {
        private int _counter;

        public AccessToken CreateAccessToken(Guid userId, string email, string role, long authVersion) =>
            new($"access-{++_counter}-{userId:N}-{role}-{authVersion}", new DateTime(2030, 1, 1, 1, 0, 0, DateTimeKind.Utc));

        public string CreateRefreshToken() => $"refresh-{++_counter}";
    }

    private sealed class FakeOAuthProvider(OAuthUserInfo userInfo) : IOAuthProvider
    {
        public string Name => "Google";
        public string BuildAuthorizationUrl(OAuthAuthorizationRequest request) =>
            throw new NotSupportedException();
        public Task<OAuthTokenResult> ExchangeCodeAsync(
            string code,
            string redirectUri,
            string codeVerifier,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new OAuthTokenResult("access-token", null, null));
        public Task<OAuthUserInfo> GetUserInfoAsync(
            OAuthTokenResult token,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(userInfo);
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public FakeUserRepository(params User[] users)
        {
            Users.AddRange(users);
        }

        public List<User> Users { get; } = [];
        public List<ExternalUser> ExternalUsers { get; } = [];

        public bool FailNextGetById { get; set; }

        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            if (FailNextGetById)
            {
                FailNextGetById = false;
                throw new InvalidOperationException("Transient database failure");
            }

            return Task.FromResult(Users.SingleOrDefault(user => user.Id == id));
        }

        public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult(Users.SingleOrDefault(user => user.Email == normalizedEmail));

        public Task<User?> GetByExternalLoginAsync(ExternalProvider provider, string externalUserId, CancellationToken cancellationToken) =>
            Task.FromResult(ExternalUsers
                .Where(external => external.Provider == provider && external.ExternalUserId == externalUserId)
                .Select(external => Users.Single(user => user.Id == external.UserId))
                .SingleOrDefault());

        public Task<ExternalUser?> GetExternalLoginAsync(Guid userId, ExternalProvider provider, CancellationToken cancellationToken) =>
            Task.FromResult(ExternalUsers.SingleOrDefault(external =>
                external.UserId == userId && external.Provider == provider));

        public void Add(User userToAdd) => Users.Add(userToAdd);
        public void AddExternalUser(ExternalUser externalUser) => ExternalUsers.Add(externalUser);
    }

    private sealed class FakeCache : ICache
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, object> _values = [];
        private readonly Dictionary<string, TimeSpan> _expirations = [];

        public string LastRefreshKey { get; private set; } = string.Empty;
        public string LastOAuthLoginCodeKey { get; private set; } = string.Empty;
        public string VersionKey => _values.Keys.Single(key => key.StartsWith("auth:version:", StringComparison.Ordinal));

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                return Task.FromResult(_values.TryGetValue(key, out var value) ? (T?)value : default);
            }
        }

        public Task<T?> ConsumeAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                if (!_values.Remove(key, out var value))
                {
                    return Task.FromResult<T?>(default);
                }

                _expirations.Remove(key);
                return Task.FromResult((T?)value);
            }
        }

        public Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                Store(key, value!, expiration);
            }

            return Task.CompletedTask;
        }

        public Task<bool> SetIfNotExistsAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                if (_values.ContainsKey(key))
                {
                    return Task.FromResult(false);
                }

                Store(key, value!, expiration);
                return Task.FromResult(true);
            }
        }

        public Task<long> IncrementAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                var value = _values.TryGetValue(key, out var existing) ? (long)existing + 1 : 1;
                _values[key] = value;
                _expirations[key] = expiration;
                return Task.FromResult(value);
            }
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                _values.Remove(key);
                _expirations.Remove(key);
            }

            return Task.CompletedTask;
        }

        public TimeSpan GetExpiry(string key) => _expirations[key];
        public T GetValue<T>(string key) => (T)_values[key];

        private void Store(string key, object value, TimeSpan expiration)
        {
            _values[key] = value;
            _expirations[key] = expiration;
            if (key.StartsWith("auth:refresh:", StringComparison.Ordinal))
            {
                LastRefreshKey = key;
            }

            if (key.StartsWith("auth:oauth-login-code:", StringComparison.Ordinal))
            {
                LastOAuthLoginCodeKey = key;
            }
        }
    }
}

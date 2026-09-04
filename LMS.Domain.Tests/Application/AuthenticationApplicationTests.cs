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
    public async Task InactiveAccountCannotLogin()
    {
        var fixture = CreateFixture(AccountStatus.Inactive);

        await Assert.ThrowsAsync<UnauthorizeException>(() =>
            fixture.Service.LoginAsync(new LoginRequest("user@example.com", "password123")));
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
        return new Fixture(service, cache);
    }

    private sealed record Fixture(AuthenticationService Service, FakeCache Cache);

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

    private sealed class FakeUserRepository(User user) : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<User?>(user.Id == id ? user : null);

        public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult<User?>(user.Email == normalizedEmail ? user : null);

        public Task<User?> GetByExternalLoginAsync(ExternalProvider provider, string externalUserId, CancellationToken cancellationToken) =>
            Task.FromResult<User?>(null);

        public void Add(User userToAdd) => throw new NotSupportedException();
        public void AddExternalUser(ExternalUser externalUser) => throw new NotSupportedException();
    }

    private sealed class FakeCache : ICache
    {
        private readonly Dictionary<string, object> _values = [];
        private readonly Dictionary<string, TimeSpan> _expirations = [];

        public string LastRefreshKey { get; private set; } = string.Empty;
        public string VersionKey => _values.Keys.Single(key => key.StartsWith("auth:version:", StringComparison.Ordinal));

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? (T?)value : default);

        public Task<T?> ConsumeAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (!_values.Remove(key, out var value))
            {
                return Task.FromResult<T?>(default);
            }

            return Task.FromResult((T?)value);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            _values[key] = value!;
            _expirations[key] = expiration;
            if (key.StartsWith("auth:refresh:", StringComparison.Ordinal))
            {
                LastRefreshKey = key;
            }

            return Task.CompletedTask;
        }

        public Task<bool> SetIfNotExistsAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            if (_values.ContainsKey(key))
            {
                return Task.FromResult(false);
            }

            _values[key] = value!;
            _expirations[key] = expiration;
            return Task.FromResult(true);
        }

        public Task<long> IncrementAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            var value = _values.TryGetValue(key, out var existing) ? (long)existing + 1 : 1;
            _values[key] = value;
            _expirations[key] = expiration;
            return Task.FromResult(value);
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            _expirations.Remove(key);
            return Task.CompletedTask;
        }

        public TimeSpan GetExpiry(string key) => _expirations[key];
        public T GetValue<T>(string key) => (T)_values[key];
    }
}
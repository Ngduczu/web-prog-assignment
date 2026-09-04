using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.caching;
using LMS.Api.application.abstractions.mail;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.time;
using LMS.Api.application.features.registration;
using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Domain.Tests.Application;

public class RegistrationApplicationTests
{
    [Fact]
    public async Task RegisterCreatesInactiveAccountAndSendsExpiringCode()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.RegisterAsync(new RegisterAccountRequest(
            " User@Example.com ",
            "password123",
            "password123"));

        var user = Assert.Single(fixture.Users.Items);
        Assert.Equal(user.Id, result.UserId);
        Assert.Equal("user@example.com", user.Email);
        Assert.Equal(AccountStatus.Inactive, user.Status);
        Assert.Equal("user@example.com", fixture.Mail.LastEmail);
        Assert.Equal(6, fixture.Mail.LastCode.Length);
        Assert.Equal(TimeSpan.FromMinutes(15), fixture.Cache.GetExpiry(fixture.Cache.LastVerificationKey));
    }

    [Fact]
    public async Task CorrectCodeActivatesAccountAndRemovesVerificationState()
    {
        var fixture = CreateFixture();
        await fixture.Service.RegisterAsync(new RegisterAccountRequest(
            "user@example.com", "password123", "password123"));

        await fixture.Service.VerifyEmailAsync(new VerifyEmailRequest(
            "USER@example.com", fixture.Mail.LastCode));

        Assert.Equal(AccountStatus.Active, Assert.Single(fixture.Users.Items).Status);
        Assert.Empty(fixture.Cache.Values);
    }

    [Fact]
    public async Task ResendIsThrottledAndInvalidCodeIsCounted()
    {
        var fixture = CreateFixture();
        await fixture.Service.RegisterAsync(new RegisterAccountRequest(
            "user@example.com", "password123", "password123"));

        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.ResendVerificationAsync(new ResendVerificationRequest("user@example.com")));

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Assert.ThrowsAsync<BadRequestException>(() =>
                fixture.Service.VerifyEmailAsync(new VerifyEmailRequest("user@example.com", "AAAAAA")));
        }

        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.VerifyEmailAsync(new VerifyEmailRequest("user@example.com", "AAAAAA")));
    }

    private static Fixture CreateFixture()
    {
        var users = new FakeUserRepository();
        var cache = new FakeCache();
        var mail = new FakeMailSender();
        var service = new RegistrationService(
            users,
            new FakePasswordHasher(),
            cache,
            mail,
            new FakeUnitOfWork(),
            new FakeClock());
        return new Fixture(service, users, cache, mail);
    }

    private sealed record Fixture(
        RegistrationService Service,
        FakeUserRepository Users,
        FakeCache Cache,
        FakeMailSender Mail);

    private sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; } = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public string Hash(string value) => $"hash:{value}";
        public bool Verify(string value, string hash) => hash == $"hash:{value}";
    }

    private sealed class FakeMailSender : IMailSender
    {
        public string LastEmail { get; private set; } = string.Empty;
        public string LastCode { get; private set; } = string.Empty;

        public Task SendEmailVerificationAsync(
            string email,
            string recipientName,
            string verificationCode,
            CancellationToken cancellationToken = default)
        {
            LastEmail = email;
            LastCode = verificationCode;
            return Task.CompletedTask;
        }

        public Task SendPasswordResetCodeAsync(string email, string recipientName, string resetCode, CancellationToken cancellationToken = default)
        {
            LastEmail = email;
            LastCode = resetCode;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public List<User> Items { get; } = [];

        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(user => user.Id == id));

        public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(user => user.Email == normalizedEmail));

        public Task<User?> GetByExternalLoginAsync(ExternalProvider provider, string externalUserId, CancellationToken cancellationToken) =>
            Task.FromResult<User?>(null);

        public void Add(User user)
        {
            Items.Add(user);
        }

        public void AddExternalUser(ExternalUser externalUser) => throw new NotSupportedException();
    }

    private sealed class FakeCache : ICache
    {
        public Dictionary<string, object> Values { get; } = [];
        private readonly Dictionary<string, TimeSpan> _expirations = [];
        public string LastVerificationKey { get; private set; } = string.Empty;

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            if (!Values.TryGetValue(key, out var value))
            {
                return Task.FromResult<T?>(default);
            }

            LastVerificationKey = key.Contains("verification:", StringComparison.Ordinal) &&
                                  !key.Contains("attempts", StringComparison.Ordinal) &&
                                  !key.Contains("cooldown", StringComparison.Ordinal)
                ? key
                : LastVerificationKey;
            return Task.FromResult((T?)value);
        }

        public Task SetAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            Values[key] = value!;
            _expirations[key] = expiration;
            LastVerificationKey = key.Contains("verification:", StringComparison.Ordinal) &&
                                  !key.Contains("attempts", StringComparison.Ordinal) &&
                                  !key.Contains("cooldown", StringComparison.Ordinal)
                ? key
                : LastVerificationKey;
            return Task.CompletedTask;
        }

        public Task<bool> SetIfNotExistsAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            if (Values.ContainsKey(key))
            {
                return Task.FromResult(false);
            }

            Values[key] = value!;
            _expirations[key] = expiration;
            return Task.FromResult(true);
        }

        public Task<long> IncrementAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            var value = Values.TryGetValue(key, out var existing) ? (long)existing + 1 : 1;
            Values[key] = value;
            _expirations[key] = expiration;
            return Task.FromResult(value);
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            Values.Remove(key);
            _expirations.Remove(key);
            return Task.CompletedTask;
        }

        public TimeSpan GetExpiry(string key) => _expirations[key];
    }
}

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
    public async Task RegisterBoundsGeneratedFullNameForLongEmailLocalPart()
    {
        var fixture = CreateFixture();
        var localPart = new string('a', 101);

        await fixture.Service.RegisterAsync(new RegisterAccountRequest(
            $"{localPart}@example.com",
            "password123",
            "password123"));

        var user = Assert.Single(fixture.Users.Items);
        Assert.Equal(100, user.FullName.Length);
        Assert.Equal(localPart[..100], user.FullName);
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

    [Theory]
    [InlineData(AccountStatus.Active)]
    [InlineData(AccountStatus.Disabled)]
    [InlineData(AccountStatus.Banned)]
    [InlineData((AccountStatus)999)]
    public async Task NonInactiveAccountCannotVerifyOrRequestNewCode(AccountStatus invalidStatus)
    {
        var fixture = CreateFixture();
        await fixture.Service.RegisterAsync(new RegisterAccountRequest(
            "user@example.com", "password123", "password123"));
        var user = Assert.Single(fixture.Users.Items);
        user.SetStatus(invalidStatus);

        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.VerifyEmailAsync(new VerifyEmailRequest(
                user.Email,
                fixture.Mail.LastCode)));
        Assert.Equal(invalidStatus, user.Status);

        await fixture.Cache.RemoveAsync($"auth:email-verification-cooldown:{user.Id:N}");
        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Service.ResendVerificationAsync(new ResendVerificationRequest(user.Email)));
        Assert.Equal(invalidStatus, user.Status);
    }

    [Fact]
    public async Task ExpiredVerificationCodeIsRejected()
    {
        var fixture = CreateFixture();
        await fixture.Service.RegisterAsync(new RegisterAccountRequest(
            "user@example.com", "password123", "password123"));
        var user = Assert.Single(fixture.Users.Items);
        await fixture.Cache.RemoveAsync(fixture.Cache.LastVerificationKey);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.VerifyEmailAsync(new VerifyEmailRequest(user.Email, fixture.Mail.LastCode)));

        Assert.Equal(AccountStatus.Inactive, user.Status);
    }

    [Fact]
    public async Task UsedVerificationCodeCannotBeReused()
    {
        var fixture = CreateFixture();
        await fixture.Service.RegisterAsync(new RegisterAccountRequest(
            "user@example.com", "password123", "password123"));
        var user = Assert.Single(fixture.Users.Items);
        var code = fixture.Mail.LastCode;
        await fixture.Service.VerifyEmailAsync(new VerifyEmailRequest(user.Email, code));
        user.SetStatus(AccountStatus.Inactive);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.VerifyEmailAsync(new VerifyEmailRequest(user.Email, code)));
    }

    [Fact]
    public async Task VerificationCodeIsRestoredWhenDatabaseSaveFails()
    {
        var fixture = CreateFixture();
        await fixture.Service.RegisterAsync(new RegisterAccountRequest(
            "user@example.com", "password123", "password123"));
        var user = Assert.Single(fixture.Users.Items);
        fixture.UnitOfWork.FailNext = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.VerifyEmailAsync(new VerifyEmailRequest(user.Email, fixture.Mail.LastCode)));

        Assert.Equal(AccountStatus.Inactive, user.Status);
        Assert.True(fixture.Cache.Values.ContainsKey(fixture.Cache.LastVerificationKey));
    }

    [Fact]
    public async Task ConcurrentVerificationCodeRedemptionHasExactlyOneWinner()
    {
        var fixture = CreateFixture();
        await fixture.Service.RegisterAsync(new RegisterAccountRequest(
            "user@example.com", "password123", "password123"));

        async Task<bool> TryVerifyAsync()
        {
            try
            {
                await fixture.Service.VerifyEmailAsync(new VerifyEmailRequest(
                    "user@example.com",
                    fixture.Mail.LastCode));
                return true;
            }
            catch (Exception exception) when (exception is ConflictException or BadRequestException)
            {
                return false;
            }
        }

        var results = await Task.WhenAll(TryVerifyAsync(), TryVerifyAsync());

        Assert.Single(results, succeeded => succeeded);
        Assert.Equal(AccountStatus.Active, Assert.Single(fixture.Users.Items).Status);
    }

    private static Fixture CreateFixture()
    {
        var users = new FakeUserRepository();
        var cache = new FakeCache();
        var mail = new FakeMailSender();
        var unitOfWork = new FakeUnitOfWork();
        var service = new RegistrationService(
            users,
            new FakePasswordHasher(),
            cache,
            mail,
            unitOfWork,
            new FakeClock());
        return new Fixture(service, users, cache, mail, unitOfWork);
    }

    private sealed record Fixture(
        RegistrationService Service,
        FakeUserRepository Users,
        FakeCache Cache,
        FakeMailSender Mail,
        FakeUnitOfWork UnitOfWork);

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
        public bool FailNext { get; set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!FailNext)
            {
                return Task.FromResult(1);
            }

            FailNext = false;
            return Task.FromException<int>(new InvalidOperationException("Database failure"));
        }
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

        public Task<ExternalUser?> GetExternalLoginAsync(Guid userId, ExternalProvider provider, CancellationToken cancellationToken) =>
            Task.FromResult<ExternalUser?>(null);

        public void Add(User user)
        {
            Items.Add(user);
        }

        public void AddExternalUser(ExternalUser externalUser) => throw new NotSupportedException();
    }

    private sealed class FakeCache : ICache
    {
        private readonly object _sync = new();
        public Dictionary<string, object> Values { get; } = [];
        private readonly Dictionary<string, TimeSpan> _expirations = [];
        public string LastVerificationKey { get; private set; } = string.Empty;

        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                if (!Values.TryGetValue(key, out var value))
                {
                    return Task.FromResult<T?>(default);
                }

                TrackVerificationKey(key);
                return Task.FromResult((T?)value);
            }
        }

        public Task<T?> ConsumeAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                if (!Values.Remove(key, out var value))
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
                Values[key] = value!;
                _expirations[key] = expiration;
                TrackVerificationKey(key);
            }

            return Task.CompletedTask;
        }

        public Task<bool> SetIfNotExistsAsync<T>(string key, T value, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                if (Values.ContainsKey(key))
                {
                    return Task.FromResult(false);
                }

                Values[key] = value!;
                _expirations[key] = expiration;
                return Task.FromResult(true);
            }
        }

        public Task<long> IncrementAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                var value = Values.TryGetValue(key, out var existing) ? (long)existing + 1 : 1;
                Values[key] = value;
                _expirations[key] = expiration;
                return Task.FromResult(value);
            }
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                Values.Remove(key);
                _expirations.Remove(key);
            }

            return Task.CompletedTask;
        }

        public TimeSpan GetExpiry(string key) => _expirations[key];

        private void TrackVerificationKey(string key)
        {
            LastVerificationKey = key.Contains("verification:", StringComparison.Ordinal) &&
                                  !key.Contains("attempts", StringComparison.Ordinal) &&
                                  !key.Contains("cooldown", StringComparison.Ordinal)
                ? key
                : LastVerificationKey;
        }
    }
}

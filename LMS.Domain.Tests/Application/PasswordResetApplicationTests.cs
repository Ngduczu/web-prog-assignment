using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.caching;
using LMS.Api.application.abstractions.mail;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.time;
using LMS.Api.application.features.passwordreset;
using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Domain.Tests.Application;

public sealed class PasswordResetApplicationTests
{
    [Fact]
    public async Task RepeatedRequestDuringCooldownDoesNotReplaceCodeOrSendAnotherEmail()
    {
        var fixture = CreateFixture();

        await fixture.Service.RequestAsync(new ForgotPasswordRequest("user@example.com"));
        var firstCode = fixture.Mail.LastCode;
        await fixture.Service.RequestAsync(new ForgotPasswordRequest("user@example.com"));

        Assert.Equal(1, fixture.Mail.PasswordResetMessagesSent);
        await fixture.Service.ResetAsync(new ResetPasswordRequest(
            "user@example.com",
            firstCode,
            "new-password123",
            "new-password123"));
    }

    [Fact]
    public async Task RequestingNewCodeDoesNotResetFailedAttemptLimit()
    {
        var fixture = CreateFixture();
        await fixture.Service.RequestAsync(new ForgotPasswordRequest("user@example.com"));
        var invalidCode = DifferentCode(fixture.Mail.LastCode);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.ResetAsync(
                new ResetPasswordRequest("user@example.com", invalidCode, "new-password123", "new-password123")));
        }

        await fixture.Cache.RemoveAsync($"auth:password-reset-cooldown:{fixture.User.Id:N}");
        await fixture.Service.RequestAsync(new ForgotPasswordRequest("user@example.com"));

        Assert.Equal(2, fixture.Mail.PasswordResetMessagesSent);
        await Assert.ThrowsAsync<ConflictException>(() => fixture.Service.ResetAsync(
            new ResetPasswordRequest(
                "user@example.com",
                fixture.Mail.LastCode,
                "new-password123",
                "new-password123")));
    }

    [Fact]
    public async Task ResetCodeAndPasswordAreRestoredWhenDatabaseSaveFails()
    {
        var fixture = CreateFixture();
        await fixture.Service.RequestAsync(new ForgotPasswordRequest("user@example.com"));
        var code = fixture.Mail.LastCode;
        fixture.UnitOfWork.FailNext = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ResetAsync(
            new ResetPasswordRequest("user@example.com", code, "new-password123", "new-password123")));

        Assert.Equal("hash:password123", fixture.User.PasswordHash);
        await fixture.Service.ResetAsync(new ResetPasswordRequest(
            "user@example.com", code, "new-password123", "new-password123"));
        Assert.Equal("hash:new-password123", fixture.User.PasswordHash);
    }

    [Fact]
    public async Task ForgotPasswordCanBeRetriedImmediatelyAfterMailFailure()
    {
        var fixture = CreateFixture();
        fixture.Mail.FailNext = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.RequestAsync(new ForgotPasswordRequest("user@example.com")));

        await fixture.Service.RequestAsync(new ForgotPasswordRequest("user@example.com"));
        Assert.Equal(1, fixture.Mail.PasswordResetMessagesSent);
        await fixture.Service.ResetAsync(new ResetPasswordRequest(
            "user@example.com",
            fixture.Mail.LastCode,
            "new-password123",
            "new-password123"));
    }

    private static string DifferentCode(string code) =>
        $"{(code[0] == 'A' ? 'B' : 'A')}{code[1..]}";

    private static Fixture CreateFixture()
    {
        var user = new User(
            "user@example.com",
            "User",
            null,
            "hash:password123",
            Role.Student,
            AccountStatus.Active);
        var users = new FakeUserRepository(user);
        var cache = new FakeCache();
        var mail = new FakeMailSender();
        var unitOfWork = new FakeUnitOfWork();
        var service = new PasswordResetService(
            users,
            new FakePasswordHasher(),
            cache,
            mail,
            unitOfWork,
            new FakeClock());
        return new Fixture(service, user, cache, mail, unitOfWork);
    }

    private sealed record Fixture(
        PasswordResetService Service,
        User User,
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
        public bool FailNext { get; set; }
        public int PasswordResetMessagesSent { get; private set; }
        public string LastCode { get; private set; } = string.Empty;

        public Task SendEmailVerificationAsync(
            string email,
            string recipientName,
            string verificationCode,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SendPasswordResetCodeAsync(
            string email,
            string recipientName,
            string resetCode,
            CancellationToken cancellationToken = default)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("SMTP failure");
            }

            PasswordResetMessagesSent++;
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

    private sealed class FakeUserRepository(User user) : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(id == user.Id ? user : null);

        public Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult(normalizedEmail == user.Email ? user : null);

        public Task<User?> GetByExternalLoginAsync(
            ExternalProvider provider,
            string externalUserId,
            CancellationToken cancellationToken) => Task.FromResult<User?>(null);

        public Task<ExternalUser?> GetExternalLoginAsync(
            Guid userId,
            ExternalProvider provider,
            CancellationToken cancellationToken) => Task.FromResult<ExternalUser?>(null);

        public void Add(User userToAdd) => throw new NotSupportedException();
        public void AddExternalUser(ExternalUser externalUser) => throw new NotSupportedException();
    }

    private sealed class FakeCache : ICache
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, object> _values = [];

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
                return Task.FromResult(_values.Remove(key, out var value) ? (T?)value : default);
            }
        }

        public Task SetAsync<T>(
            string key,
            T value,
            TimeSpan expiration,
            CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                _values[key] = value!;
            }

            return Task.CompletedTask;
        }

        public Task<bool> SetIfNotExistsAsync<T>(
            string key,
            T value,
            TimeSpan expiration,
            CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                if (_values.ContainsKey(key)) return Task.FromResult(false);
                _values[key] = value!;
                return Task.FromResult(true);
            }
        }

        public Task<long> IncrementAsync(
            string key,
            TimeSpan expiration,
            CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                var value = _values.TryGetValue(key, out var existing) ? (long)existing + 1 : 1;
                _values[key] = value;
                return Task.FromResult(value);
            }
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                _values.Remove(key);
            }

            return Task.CompletedTask;
        }
    }
}

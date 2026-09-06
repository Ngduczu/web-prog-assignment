using System.Net.Mail;
using System.Security.Cryptography;
using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.caching;
using LMS.Api.application.abstractions.mail;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.time;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Api.application.features.passwordreset;

public sealed class PasswordResetService : IPasswordResetService
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int MaxAttempts = 5;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RequestCooldown = TimeSpan.FromMinutes(1);
    private readonly IUserRepository _users; private readonly IPasswordHasher _hasher; private readonly ICache _cache; private readonly IMailSender _mail; private readonly IUnitOfWork _uow; private readonly IClock _clock;
    public PasswordResetService(IUserRepository users, IPasswordHasher hasher, ICache cache, IMailSender mail, IUnitOfWork uow, IClock clock) { _users = users; _hasher = hasher; _cache = cache; _mail = mail; _uow = uow; _clock = clock; }

    public async Task<PasswordResetRequestedDto> RequestAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var email = NormalizeEmail(request.Email);
        var expires = _clock.UtcNow.Add(Ttl);
        var user = await _users.GetByEmailAsync(email, cancellationToken);
        if (user is null || user.PasswordHash is null || user.Status != AccountStatus.Active) return new PasswordResetRequestedDto(true, expires);
        var cooldownAcquired = await _cache.SetIfNotExistsAsync(
            CooldownKey(user.Id),
            true,
            RequestCooldown,
            cancellationToken);
        if (!cooldownAcquired) return new PasswordResetRequestedDto(true, expires);
        var code = GenerateCode();
        await _cache.SetAsync(Key(user.Id), new ResetEntry(user.Id, user.Email, _hasher.Hash(code)), Ttl, cancellationToken);
        await _mail.SendPasswordResetCodeAsync(user.Email, user.FullName, code, cancellationToken);
        return new PasswordResetRequestedDto(true, expires);
    }

    public async Task<PasswordResetResultDto> ResetAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var email = NormalizeEmail(request.Email); ValidatePassword(request.NewPassword, request.PasswordConfirm); ValidateCode(request.ResetCode);
        var user = await _users.GetByEmailAsync(email, cancellationToken) ?? throw new BadRequestException("Reset code is invalid or expired.");
        if (user.PasswordHash is null) throw new ConflictException("OAuth accounts do not use password reset.");
        var entry = await _cache.GetAsync<ResetEntry>(Key(user.Id), cancellationToken) ?? throw new BadRequestException("Reset code is invalid or expired.");
        if (await _cache.IncrementAsync(AttemptsKey(user.Id), Ttl, cancellationToken) > MaxAttempts) { await _cache.RemoveAsync(Key(user.Id), cancellationToken); throw new ConflictException("Too many reset attempts. Try again later."); }
        if (entry.UserId != user.Id || entry.Email != user.Email || !_hasher.Verify(request.ResetCode.Trim().ToUpperInvariant(), entry.CodeHash)) throw new BadRequestException("Reset code is invalid or expired.");
        if (await _cache.ConsumeAsync<ResetEntry>(Key(user.Id), cancellationToken) is null) throw new ConflictException("Reset code has already been used. Request a new code.");
        user.SetPasswordHash(_hasher.Hash(request.NewPassword)); await _uow.SaveChangesAsync(cancellationToken);
        await _cache.SetIfNotExistsAsync($"auth:version:{user.Id:N}", 1L, TimeSpan.FromDays(365), cancellationToken);
        await _cache.IncrementAsync($"auth:version:{user.Id:N}", TimeSpan.FromDays(365), cancellationToken);
        await _cache.RemoveAsync(AttemptsKey(user.Id), cancellationToken);
        await _cache.RemoveAsync(CooldownKey(user.Id), cancellationToken);
        return new PasswordResetResultDto(user.Id, user.Email, user.Status, _clock.UtcNow);
    }
    private static string GenerateCode() { Span<char> chars = stackalloc char[6]; for (var i = 0; i < chars.Length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]; return new string(chars); }
    private static string NormalizeEmail(string email) { var value = email?.Trim().ToLowerInvariant(); if (string.IsNullOrWhiteSpace(value) || !MailAddress.TryCreate(value, out var address) || address.Address != value) throw new BadRequestException("Email address is invalid."); return value; }
    private static void ValidatePassword(string password, string confirm) { if (string.IsNullOrEmpty(password) || password.Length is < 8 or > 128 || password != confirm) throw new BadRequestException("Password is invalid or confirmation does not match."); }
    private static void ValidateCode(string code) { if (string.IsNullOrWhiteSpace(code) || code.Trim().Length != 6 || code.Trim().Any(c => !Alphabet.Contains(char.ToUpperInvariant(c)))) throw new BadRequestException("Reset code must contain exactly 6 supported characters."); }
    private static string Key(Guid id) => $"auth:password-reset:{id:N}";
    private static string AttemptsKey(Guid id) => $"auth:password-reset-attempts:{id:N}";
    private static string CooldownKey(Guid id) => $"auth:password-reset-cooldown:{id:N}";
    private sealed record ResetEntry(Guid UserId, string Email, string CodeHash);
}

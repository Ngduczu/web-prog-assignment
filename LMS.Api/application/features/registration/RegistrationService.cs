using System.Net.Mail;
using System.Security.Cryptography;
using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.caching;
using LMS.Api.application.abstractions.mail;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.time;
using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Api.application.features.registration;

public sealed class RegistrationService : IRegistrationService
{
    private const string VerificationAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int VerificationCodeLength = 6;
    private const int MaximumVerificationAttempts = 5;
    private static readonly TimeSpan VerificationTimeToLive = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(1);
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICache _cache;
    private readonly IMailSender _mailSender;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public RegistrationService(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        ICache cache,
        IMailSender mailSender,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _cache = cache;
        _mailSender = mailSender;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<RegistrationDto> RegisterAsync(
        RegisterAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRegistration(request);
        var normalizedEmail = NormalizeEmail(request.Email);

        if (await _users.GetByEmailAsync(normalizedEmail, cancellationToken) is not null)
        {
            throw new ConflictException("An account with this email already exists.");
        }

        var user = new User(
            normalizedEmail,
            normalizedEmail[..normalizedEmail.IndexOf('@')],
            null,
            _passwordHasher.Hash(request.Password),
            Role.Student,
            AccountStatus.Inactive);
        _users.Add(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var expiresAt = _clock.UtcNow.Add(VerificationTimeToLive);
        await CreateAndSendVerificationCodeAsync(user, cancellationToken);
        return new RegistrationDto(user.Id, user.Email, user.Status, expiresAt);
    }

    public async Task<EmailVerificationResultDto> VerifyEmailAsync(
        VerifyEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        ValidateVerificationCode(request.VerificationCode);
        var user = await _users.GetByEmailAsync(normalizedEmail, cancellationToken)
            ?? throw new NotFoundException("Account was not found.");

        if (user.Status != AccountStatus.Inactive)
        {
            throw new ConflictException("Only inactive accounts can be verified.");
        }

        if (user.PasswordHash is null)
        {
            throw new ConflictException("OAuth accounts do not require email verification.");
        }

        var cacheEntry = await _cache.GetAsync<EmailVerificationCacheEntry>(
            GetVerificationKey(user.Id),
            cancellationToken) ?? throw new BadRequestException("Verification code is invalid or expired.");
        var attemptCount = await _cache.IncrementAsync(
            GetAttemptsKey(user.Id),
            VerificationTimeToLive,
            cancellationToken);

        if (attemptCount > MaximumVerificationAttempts)
        {
            await _cache.RemoveAsync(GetVerificationKey(user.Id), cancellationToken);
            throw new ConflictException("Too many verification attempts. Request a new code.");
        }

        var normalizedCode = request.VerificationCode.Trim().ToUpperInvariant();
        if (cacheEntry.UserId != user.Id ||
            cacheEntry.Email != user.Email ||
            !_passwordHasher.Verify(normalizedCode, cacheEntry.CodeHash))
        {
            throw new BadRequestException("Verification code is invalid or expired.");
        }

        if (await _cache.ConsumeAsync<EmailVerificationCacheEntry>(GetVerificationKey(user.Id), cancellationToken) is null)
        {
            throw new ConflictException("Verification code has already been used. Request a new code.");
        }

        user.SetStatus(AccountStatus.Active);
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            user.SetStatus(AccountStatus.Inactive);
            await _cache.SetAsync(
                GetVerificationKey(user.Id),
                cacheEntry,
                VerificationTimeToLive,
                CancellationToken.None);
            throw;
        }
        await _cache.RemoveAsync(GetAttemptsKey(user.Id), cancellationToken);
        await _cache.RemoveAsync(GetCooldownKey(user.Id), cancellationToken);
        return new EmailVerificationResultDto(user.Id, user.Email, user.Status, _clock.UtcNow);
    }

    public async Task ResendVerificationAsync(
        ResendVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await _users.GetByEmailAsync(normalizedEmail, cancellationToken)
            ?? throw new NotFoundException("Account was not found.");

        if (user.Status != AccountStatus.Inactive)
        {
            throw new ConflictException("Only inactive accounts can request a verification code.");
        }

        if (user.PasswordHash is null)
        {
            throw new ConflictException("OAuth accounts do not require email verification.");
        }

        var cooldownAcquired = await _cache.SetIfNotExistsAsync(
            GetCooldownKey(user.Id),
            true,
            ResendCooldown,
            cancellationToken);
        if (!cooldownAcquired)
        {
            throw new ConflictException("Please wait before requesting another verification code.");
        }

        await CreateAndSendVerificationCodeAsync(user, cancellationToken, setCooldown: false);
    }

    private async Task CreateAndSendVerificationCodeAsync(
        User user,
        CancellationToken cancellationToken,
        bool setCooldown = true)
    {
        var verificationCode = GenerateVerificationCode();
        var cacheEntry = new EmailVerificationCacheEntry(
            user.Id,
            user.Email,
            _passwordHasher.Hash(verificationCode));
        await _cache.SetAsync(
            GetVerificationKey(user.Id),
            cacheEntry,
            VerificationTimeToLive,
            cancellationToken);
        await _cache.RemoveAsync(GetAttemptsKey(user.Id), cancellationToken);

        if (setCooldown)
        {
            await _cache.SetAsync(GetCooldownKey(user.Id), true, ResendCooldown, cancellationToken);
        }

        await _mailSender.SendEmailVerificationAsync(
            user.Email,
            user.FullName,
            verificationCode,
            cancellationToken);
    }

    private async Task RemoveVerificationStateAsync(Guid userId, CancellationToken cancellationToken)
    {
        await _cache.RemoveAsync(GetVerificationKey(userId), cancellationToken);
        await _cache.RemoveAsync(GetAttemptsKey(userId), cancellationToken);
        await _cache.RemoveAsync(GetCooldownKey(userId), cancellationToken);
    }

    private static string GenerateVerificationCode()
    {
        Span<char> code = stackalloc char[VerificationCodeLength];
        for (var index = 0; index < code.Length; index++)
        {
            code[index] = VerificationAlphabet[RandomNumberGenerator.GetInt32(VerificationAlphabet.Length)];
        }

        return new string(code);
    }

    private static void ValidateRegistration(RegisterAccountRequest request)
    {
        _ = NormalizeEmail(request.Email);

        if (string.IsNullOrEmpty(request.Password) || request.Password.Length is < 8 or > 128)
        {
            throw new BadRequestException("Password must contain between 8 and 128 characters.");
        }

        if (request.Password != request.PasswordConfirm)
        {
            throw new BadRequestException("Password confirmation does not match.");
        }

    }

    private static void ValidateVerificationCode(string verificationCode)
    {
        if (string.IsNullOrWhiteSpace(verificationCode) ||
            verificationCode.Trim().Length != VerificationCodeLength ||
            verificationCode.Trim().Any(character => !VerificationAlphabet.Contains(char.ToUpperInvariant(character))))
        {
            throw new BadRequestException("Verification code must contain exactly 6 supported letters or numbers.");
        }
    }

    private static string NormalizeEmail(string email)
    {
        var normalizedEmail = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail) ||
            normalizedEmail.Length > 255 ||
            !MailAddress.TryCreate(normalizedEmail, out var address) ||
            address.Address != normalizedEmail)
        {
            throw new BadRequestException("Email address is invalid.");
        }

        return normalizedEmail;
    }

    private static string GetVerificationKey(Guid userId) => $"auth:email-verification:{userId:N}";
    private static string GetAttemptsKey(Guid userId) => $"auth:email-verification-attempts:{userId:N}";
    private static string GetCooldownKey(Guid userId) => $"auth:email-verification-cooldown:{userId:N}";

    private sealed record EmailVerificationCacheEntry(Guid UserId, string Email, string CodeHash);
}

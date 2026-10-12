using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.caching;
using LMS.Api.application.abstractions.mail;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.time;
using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;
using LMS.Api.application.features.auth.utils;

namespace LMS.Api.application.features.registration;

public sealed class RegistrationService : IRegistrationService
{
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
        var normalizedEmail = AuthValidation.NormalizeEmail(request.Email);
        var normalizedFullName = request.FullName?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedFullName) || normalizedFullName.Length > 100)
        {
            throw new BadRequestException("Full name must contain between 1 and 100 characters.");
        }

        var normalizedPhone = string.IsNullOrWhiteSpace(request.Phone)
            ? null
            : request.Phone.Trim();
        if (normalizedPhone?.Length > 20)
        {
            throw new BadRequestException("Phone cannot exceed 20 characters.");
        }

        if (!AuthValidation.IsPasswordValid(request.Password))
        {
            throw new BadRequestException("Password must contain between 8 and 128 characters.");
        }

        if (request.Password != request.PasswordConfirm)
        {
            throw new BadRequestException("Password confirmation does not match.");
        }

        var existingUser = await _users.GetByEmailAsync(normalizedEmail, cancellationToken);
        if (existingUser is not null)
        {
            if (existingUser.Status == AccountStatus.Inactive && existingUser.PasswordHash is not null)
            {
                if (!_passwordHasher.Verify(request.Password, existingUser.PasswordHash))
                {
                    throw new ConflictException(
                        "The registration request does not match the existing inactive account.");
                }

                await AcquireCooldownAndSendVerificationCodeAsync(existingUser, cancellationToken);
                return new RegistrationDto(
                    existingUser.Id,
                    existingUser.Email,
                    existingUser.Status,
                    _clock.UtcNow.Add(VerificationTimeToLive));
            }

            throw new ConflictException("An account with this email already exists.");
        }

        var user = new User(
            normalizedEmail,
            normalizedFullName,
            normalizedPhone,
            _passwordHasher.Hash(request.Password),
            Role.Student,
            AccountStatus.Inactive);
        _users.Add(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var expiresAt = _clock.UtcNow.Add(VerificationTimeToLive);
        await AcquireCooldownAndSendVerificationCodeAsync(user, cancellationToken);
        return new RegistrationDto(user.Id, user.Email, user.Status, expiresAt);
    }

    public async Task<EmailVerificationResultDto> VerifyEmailAsync(
        VerifyEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = AuthValidation.NormalizeEmail(request.Email);
        if (!AuthValidation.IsHumanCodeValid(request.VerificationCode))
        {
            throw new BadRequestException(
                "Verification code must contain exactly 6 supported letters or numbers.");
        }
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
            AuthCacheKeys.EmailVerification(user.Id),
            cancellationToken) ?? throw new BadRequestException("Verification code is invalid or expired.");
        var attemptCount = await _cache.IncrementAsync(
            AuthCacheKeys.EmailVerificationAttempts(user.Id),
            VerificationTimeToLive,
            cancellationToken);

        if (attemptCount > MaximumVerificationAttempts)
        {
            await _cache.RemoveAsync(AuthCacheKeys.EmailVerification(user.Id), cancellationToken);
            throw new ConflictException("Too many verification attempts. Request a new code.");
        }

        var normalizedCode = request.VerificationCode.Trim().ToUpperInvariant();
        if (cacheEntry.UserId != user.Id ||
            cacheEntry.Email != user.Email ||
            !_passwordHasher.Verify(normalizedCode, cacheEntry.CodeHash))
        {
            throw new BadRequestException("Verification code is invalid or expired.");
        }

        if (await _cache.ConsumeAsync<EmailVerificationCacheEntry>(AuthCacheKeys.EmailVerification(user.Id), cancellationToken) is null)
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
                AuthCacheKeys.EmailVerification(user.Id),
                cacheEntry,
                VerificationTimeToLive,
                CancellationToken.None);
            throw;
        }
        await _cache.RemoveAsync(AuthCacheKeys.EmailVerificationAttempts(user.Id), cancellationToken);
        await _cache.RemoveAsync(AuthCacheKeys.EmailVerificationCooldown(user.Id), cancellationToken);
        return new EmailVerificationResultDto(user.Id, user.Email, user.Status, _clock.UtcNow);
    }

    public async Task ResendVerificationAsync(
        ResendVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = AuthValidation.NormalizeEmail(request.Email);
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

        await AcquireCooldownAndSendVerificationCodeAsync(user, cancellationToken);
    }

    private async Task AcquireCooldownAndSendVerificationCodeAsync(
        User user,
        CancellationToken cancellationToken)
    {
        var cooldownAcquired = await _cache.SetIfNotExistsAsync(
            AuthCacheKeys.EmailVerificationCooldown(user.Id),
            true,
            ResendCooldown,
            cancellationToken);
        if (!cooldownAcquired)
        {
            throw new ConflictException("Please wait before requesting another verification code.");
        }

        await CreateAndSendVerificationCodeAsync(user, cancellationToken);
    }

    private async Task CreateAndSendVerificationCodeAsync(
        User user,
        CancellationToken cancellationToken)
    {
        var verificationCode = AuthSecurity.GenerateHumanCode(AuthValidation.HumanCodeLength);
        var cacheEntry = new EmailVerificationCacheEntry(
            user.Id,
            user.Email,
            _passwordHasher.Hash(verificationCode));
        await _cache.SetAsync(
            AuthCacheKeys.EmailVerification(user.Id),
            cacheEntry,
            VerificationTimeToLive,
            cancellationToken);
        await _cache.RemoveAsync(AuthCacheKeys.EmailVerificationAttempts(user.Id), cancellationToken);

        try
        {
            await _mailSender.SendEmailVerificationAsync(
                user.Email,
                user.FullName,
                verificationCode,
                cancellationToken);
        }
        catch
        {
            await RemoveVerificationStateAsync(user.Id, CancellationToken.None);
            throw;
        }
    }

    private async Task RemoveVerificationStateAsync(Guid userId, CancellationToken cancellationToken)
    {
        await _cache.RemoveAsync(AuthCacheKeys.EmailVerification(userId), cancellationToken);
        await _cache.RemoveAsync(AuthCacheKeys.EmailVerificationAttempts(userId), cancellationToken);
        await _cache.RemoveAsync(AuthCacheKeys.EmailVerificationCooldown(userId), cancellationToken);
    }

}

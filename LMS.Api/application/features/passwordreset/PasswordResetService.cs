using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.caching;
using LMS.Api.application.abstractions.mail;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.time;
using LMS.Api.application.features.auth;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Api.application.features.passwordreset;

public sealed class PasswordResetService : IPasswordResetService
{
    private const int MaximumAttempts = 5;
    private static readonly TimeSpan ResetLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RequestCooldown = TimeSpan.FromMinutes(1);
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICache _cache;
    private readonly IMailSender _mailSender;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public PasswordResetService(
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

    public async Task<PasswordResetRequestedDto> RequestAsync(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = AuthValidation.NormalizeEmail(request.Email);
        var expiresAt = _clock.UtcNow.Add(ResetLifetime);
        var user = await _users.GetByEmailAsync(email, cancellationToken);
        if (user is null || user.PasswordHash is null || user.Status != AccountStatus.Active)
        {
            return new PasswordResetRequestedDto(true, expiresAt);
        }

        var cooldownAcquired = await _cache.SetIfNotExistsAsync(
            AuthCacheKeys.PasswordResetCooldown(user.Id),
            true,
            RequestCooldown,
            cancellationToken);
        if (!cooldownAcquired)
        {
            return new PasswordResetRequestedDto(true, expiresAt);
        }

        var code = AuthSecurity.GenerateHumanCode(AuthValidation.HumanCodeLength);
        await _cache.SetAsync(
            AuthCacheKeys.PasswordReset(user.Id),
            new PasswordResetCacheEntry(user.Id, user.Email, _passwordHasher.Hash(code)),
            ResetLifetime,
            cancellationToken);
        try
        {
            await _mailSender.SendPasswordResetCodeAsync(
                user.Email,
                user.FullName,
                code,
                cancellationToken);
        }
        catch
        {
            await _cache.RemoveAsync(
                AuthCacheKeys.PasswordReset(user.Id),
                CancellationToken.None);
            await _cache.RemoveAsync(
                AuthCacheKeys.PasswordResetCooldown(user.Id),
                CancellationToken.None);
            throw;
        }

        return new PasswordResetRequestedDto(true, expiresAt);
    }

    public async Task<PasswordResetResultDto> ResetAsync(
        ResetPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var email = AuthValidation.NormalizeEmail(request.Email);
        if (!AuthValidation.IsPasswordValid(request.NewPassword) ||
            request.NewPassword != request.PasswordConfirm)
        {
            throw new BadRequestException("Password is invalid or confirmation does not match.");
        }

        if (!AuthValidation.IsHumanCodeValid(request.ResetCode))
        {
            throw new BadRequestException("Reset code must contain exactly 6 supported characters.");
        }

        var user = await _users.GetByEmailAsync(email, cancellationToken)
            ?? throw new BadRequestException("Reset code is invalid or expired.");
        if (user.PasswordHash is null)
        {
            throw new ConflictException("OAuth accounts do not use password reset.");
        }

        var resetKey = AuthCacheKeys.PasswordReset(user.Id);
        var entry = await _cache.GetAsync<PasswordResetCacheEntry>(resetKey, cancellationToken)
            ?? throw new BadRequestException("Reset code is invalid or expired.");
        var attemptCount = await _cache.IncrementAsync(
            AuthCacheKeys.PasswordResetAttempts(user.Id),
            ResetLifetime,
            cancellationToken);
        if (attemptCount > MaximumAttempts)
        {
            await _cache.RemoveAsync(resetKey, cancellationToken);
            throw new ConflictException("Too many reset attempts. Try again later.");
        }

        if (entry.UserId != user.Id ||
            entry.Email != user.Email ||
            !_passwordHasher.Verify(
                request.ResetCode.Trim().ToUpperInvariant(),
                entry.CodeHash))
        {
            throw new BadRequestException("Reset code is invalid or expired.");
        }

        if (await _cache.ConsumeAsync<PasswordResetCacheEntry>(resetKey, cancellationToken) is null)
        {
            throw new ConflictException("Reset code has already been used. Request a new code.");
        }

        var oldPasswordHash = user.PasswordHash;
        var oldAuthVersion = user.AuthVersion;
        user.SetPasswordHash(_passwordHasher.Hash(request.NewPassword));
        user.RevokeSessions();
        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            user.RestorePasswordSecurityState(oldPasswordHash, oldAuthVersion);
            await _cache.SetAsync(
                resetKey,
                entry,
                ResetLifetime,
                CancellationToken.None);
            throw;
        }

        await TryRemoveAsync(AuthCacheKeys.PasswordResetAttempts(user.Id));
        await TryRemoveAsync(AuthCacheKeys.PasswordResetCooldown(user.Id));
        return new PasswordResetResultDto(
            user.Id,
            user.Email,
            user.Status,
            _clock.UtcNow);
    }

    private async Task TryRemoveAsync(string key)
    {
        try
        {
            await _cache.RemoveAsync(key, CancellationToken.None);
        }
        catch
        {
            // Password and session revocation are already committed atomically in PostgreSQL.
        }
    }
}

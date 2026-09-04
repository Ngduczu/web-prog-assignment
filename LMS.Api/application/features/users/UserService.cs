using System.Net.Mail;
using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.abstractions.caching;
using LMS.Api.application.common;
using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Api.application.features.users;

public sealed class UserService : IUserService
{
    private readonly IUserManagementRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly ICache _cache;

    public UserService(IUserManagementRepository users, IPasswordHasher hasher, IUnitOfWork unitOfWork, ICurrentUser currentUser, ICache cache)
    { _users = users; _hasher = hasher; _unitOfWork = unitOfWork; _currentUser = currentUser; _cache = cache; }

    public async Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty) throw new BadRequestException("User id is required.");
        var actorId = _currentUser.GetRequiredUserId();
        var actorRole = _currentUser.GetRequiredRole();
        if (actorRole != Role.Admin && actorId != userId)
        {
            throw new ForbiddenException("You can only view your own profile.");
        }

        var user = await _users.GetByIdAsync(userId, cancellationToken) ?? throw new NotFoundException("User was not found.");
        return Map(user);
    }

    public async Task<PagedResult<UserProfileDto>> ListAsync(UserQuery query, CancellationToken cancellationToken = default)
    {
        EnsureAdmin(); ValidatePage(query.Page, query.PageSize);
        var items = await _users.ListAsync(query.Search, (query.Page - 1) * query.PageSize, query.PageSize, cancellationToken);
        var total = await _users.CountAsync(query.Search, cancellationToken);
        return new PagedResult<UserProfileDto>(items.Select(Map).ToArray(), query.Page, query.PageSize, total);
    }

    public async Task<UserProfileDto> CreateByAdminAsync(AdminCreateUserRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        var email = NormalizeEmail(request.Email);
        if (await _users.GetByEmailAsync(email, cancellationToken) is not null) throw new ConflictException("An account with this email already exists.");
        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length > 100) throw new BadRequestException("Full name is invalid.");
        if (request.Password.Length is < 8 or > 128) throw new BadRequestException("Password must contain between 8 and 128 characters.");
        var user = new User(email, request.FullName.Trim(), request.Phone?.Trim(), _hasher.Hash(request.Password), request.Role, AccountStatus.Active, _currentUser.GetRequiredUserId());
        _users.Add(user); await _unitOfWork.SaveChangesAsync(cancellationToken); return Map(user);
    }

    public async Task<UserProfileDto> UpdateStatusAsync(Guid userId, UpdateUserStatusRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        if (userId == Guid.Empty || !Enum.IsDefined(request.Status)) throw new BadRequestException("User id or status is invalid.");
        var user = await _users.GetByIdAsync(userId, cancellationToken) ?? throw new NotFoundException("User was not found.");
        if (user.Role == Role.Admin && user.Id != _currentUser.GetRequiredUserId()) throw new ForbiddenException("Administrator accounts cannot be modified.");
        user.SetStatus(request.Status); user.SetModifiedBy(_currentUser.GetRequiredUserId());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await InvalidateTokensAsync(user.Id, cancellationToken);
        return Map(user);
    }

    public async Task<UserProfileDto> UpdateByAdminAsync(Guid userId, AdminUpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAdmin();
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length > 100 || !Enum.IsDefined(request.Role))
            throw new BadRequestException("User id, full name or role is invalid.");
        var user = await _users.GetByIdAsync(userId, cancellationToken) ?? throw new NotFoundException("User was not found.");
        user.SetFullName(request.FullName.Trim());
        user.SetPhone(request.Phone?.Trim() ?? string.Empty);
        user.SetRole(request.Role);
        user.SetModifiedBy(_currentUser.GetRequiredUserId());
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await InvalidateTokensAsync(user.Id, cancellationToken);
        return Map(user);
    }

    private void EnsureAdmin() { if (_currentUser.GetRequiredRole() != Role.Admin) throw new ForbiddenException("Only administrators can manage users."); }
    private async Task InvalidateTokensAsync(Guid userId, CancellationToken cancellationToken)
    {
        await _cache.SetIfNotExistsAsync($"auth:version:{userId:N}", 1L, TimeSpan.FromDays(365), cancellationToken);
        await _cache.IncrementAsync($"auth:version:{userId:N}", TimeSpan.FromDays(365), cancellationToken);
    }
    private static void ValidatePage(int page, int size) { if (page <= 0 || size is < 1 or > 100) throw new BadRequestException("Invalid pagination."); }
    private static string NormalizeEmail(string email) { var value = email?.Trim().ToLowerInvariant(); if (string.IsNullOrWhiteSpace(value) || !MailAddress.TryCreate(value, out var address) || address.Address != value) throw new BadRequestException("Email address is invalid."); return value; }
    private static UserProfileDto Map(User u) => new(u.Id, u.Email, u.FullName, u.Phone, u.Role, u.Status, u.AvatarUrl, u.CreatedAt);
}

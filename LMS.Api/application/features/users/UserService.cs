using System.Net.Mail;
using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.common;
using LMS.Api.application.features.auth.utils;
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
    private readonly ICourseRepository _courses;
    private readonly IQuestionBankRepository _questionBanks;
    private readonly ITransactionManager? _transactionManager;

    public UserService(IUserManagementRepository users, IPasswordHasher hasher, IUnitOfWork unitOfWork, ICurrentUser currentUser, ICourseRepository courses, IQuestionBankRepository questionBanks, ITransactionManager? transactionManager = null)
    { _users = users; _hasher = hasher; _unitOfWork = unitOfWork; _currentUser = currentUser; _courses = courses; _questionBanks = questionBanks; _transactionManager = transactionManager; }

    public async Task<UserProfileDto> GetProfileAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty) throw new BadRequestException("User id is required.");
        var actorId = _currentUser.GetRequiredUserId();
        var actorRole = _currentUser.GetRequiredRole();
        if (!actorRole.IsAdminOrSystem() && actorId != userId)
        {
            throw new ForbiddenException("You can only view your own profile.");
        }

        var user = await _users.GetByIdAsync(userId, cancellationToken) ?? throw new NotFoundException("User was not found.");
        var isGoogleLinked = await _users.GetExternalLoginAsync(
            userId,
            ExternalProvider.Google,
            cancellationToken) is not null;
        return Map(user) with { IsGoogleLinked = isGoogleLinked };
    }

    public async Task<ChatUserSearchDto> FindForChatByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        var actorId = _currentUser.GetRequiredUserId();
        var user = await _users.GetByEmailAsync(normalizedEmail, cancellationToken);
        if (user is null || user.Status != AccountStatus.Active)
            throw new NotFoundException("An active user with this email was not found.");
        if (user.Id == actorId)
            throw new BadRequestException("You cannot start a direct conversation with yourself.");
        return new ChatUserSearchDto(user.Id, user.Email, user.FullName, user.AvatarUrl, user.Role);
    }

    public async Task<UserProfileDto> UpdateMyProfileAsync(
        UpdateMyProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length > 100)
            throw new BadRequestException("Full name is invalid.");
        if (request.Phone?.Trim().Length > 20)
            throw new BadRequestException("Phone cannot exceed 20 characters.");

        var actorId = _currentUser.GetRequiredUserId();
        var user = await _users.GetByIdAsync(actorId, cancellationToken)
            ?? throw new NotFoundException("User was not found.");
        user.SetFullName(request.FullName.Trim());
        user.SetPhone(request.Phone?.Trim() ?? string.Empty);
        user.SetModifiedBy(actorId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Map(user);
    }

    public async Task ChangeMyPasswordAsync(
        ChangeMyPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!AuthValidation.IsPasswordValid(request.NewPassword) ||
            request.NewPassword != request.PasswordConfirm)
        {
            throw new BadRequestException("Password is invalid or confirmation does not match.");
        }

        var actorId = _currentUser.GetRequiredUserId();
        var user = await _users.GetByIdAsync(actorId, cancellationToken)
            ?? throw new NotFoundException("User was not found.");

        if (user.PasswordHash is not null)
        {
            if (string.IsNullOrEmpty(request.CurrentPassword) ||
                !_hasher.Verify(request.CurrentPassword, user.PasswordHash))
            {
                throw new BadRequestException("Current password is incorrect.");
            }

            if (_hasher.Verify(request.NewPassword, user.PasswordHash))
            {
                throw new BadRequestException("New password must be different from the current password.");
            }
        }

        user.SetPasswordHash(_hasher.Hash(request.NewPassword));
        user.SetModifiedBy(actorId);
        user.RevokeSessions();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<UserProfileDto>> ListAsync(UserQuery query, CancellationToken cancellationToken = default)
    {
        EnsureAdminOrSystem(); ValidatePage(query.Page, query.PageSize);
        var items = await _users.ListAsync(
            query.Search,
            query.Role,
            query.Status,
            (query.Page - 1) * query.PageSize,
            query.PageSize,
            cancellationToken);
        var total = await _users.CountAsync(query.Search, query.Role, query.Status, cancellationToken);
        return new PagedResult<UserProfileDto>(items.Select(Map).ToArray(), query.Page, query.PageSize, total);
    }

    public async Task<UserProfileDto> CreateByAdminAsync(AdminCreateUserRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAdminOrSystem();
        if (!Enum.IsDefined(request.Role)) throw new BadRequestException("Role is invalid.");
        var actorRole = _currentUser.GetRequiredRole();
        if (request.Role == Role.System || actorRole == Role.Admin && request.Role == Role.Admin)
            throw new ForbiddenException("Only the system account can create administrator accounts, and system accounts cannot be created through this endpoint.");
        var email = NormalizeEmail(request.Email);
        if (await _users.GetByEmailAsync(email, cancellationToken) is not null) throw new ConflictException("An account with this email already exists.");
        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length > 100) throw new BadRequestException("Full name is invalid.");
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length is < 8 or > 128) throw new BadRequestException("Password must contain between 8 and 128 characters.");
        if (request.Phone?.Trim().Length > 20) throw new BadRequestException("Phone cannot exceed 20 characters.");
        var user = new User(email, request.FullName.Trim(), request.Phone?.Trim(), _hasher.Hash(request.Password), request.Role, AccountStatus.Active, _currentUser.GetRequiredUserId());
        _users.Add(user); await _unitOfWork.SaveChangesAsync(cancellationToken); return Map(user);
    }

    public async Task<UserProfileDto> UpdateStatusAsync(Guid userId, UpdateUserStatusRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAdminOrSystem();
        if (userId == Guid.Empty || !Enum.IsDefined(request.Status)) throw new BadRequestException("User id or status is invalid.");
        if (request.Status == AccountStatus.Inactive) throw new BadRequestException("The inactive status can only be set during registration.");
        UserProfileDto? result = null;
        async Task UpdateCore(CancellationToken transactionCancellationToken)
        {
            var user = await _users.GetByIdAsync(userId, transactionCancellationToken) ?? throw new NotFoundException("User was not found.");
            var actorId = _currentUser.GetRequiredUserId();
            var actorRole = _currentUser.GetRequiredRole();
            EnsureManageableTarget(actorRole, user.Role);
            if (user.Role == Role.Admin && user.Status == AccountStatus.Active && request.Status != AccountStatus.Active &&
                await _users.CountActiveByRoleAsync(Role.Admin, transactionCancellationToken) <= 1)
                throw new ConflictException("The last active administrator cannot be disabled.");
            if (user.Role == Role.Teacher && request.Status != AccountStatus.Active)
                await EnsureTeacherHasNoOwnedResourcesAsync(user.Id, transactionCancellationToken);
            user.SetStatus(request.Status); user.SetModifiedBy(actorId); user.RevokeSessions();
            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
            result = Map(user);
        }

        await ExecuteSerializableAsync(UpdateCore, cancellationToken);
        return result!;
    }

    public async Task<UserProfileDto> UpdateByAdminAsync(Guid userId, AdminUpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        EnsureAdminOrSystem();
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length > 100 || !Enum.IsDefined(request.Role) || request.Phone?.Trim().Length > 20)
            throw new BadRequestException("User id, full name or role is invalid.");
        UserProfileDto? result = null;
        async Task UpdateCore(CancellationToken transactionCancellationToken)
        {
            var user = await _users.GetByIdAsync(userId, transactionCancellationToken) ?? throw new NotFoundException("User was not found.");
            var actorId = _currentUser.GetRequiredUserId();
            var actorRole = _currentUser.GetRequiredRole();
            EnsureManageableTarget(actorRole, user.Role);
            if (actorRole == Role.Admin && request.Role is not (Role.Student or Role.Teacher))
                throw new ForbiddenException("Administrators can only assign the Student or Teacher role.");
            if (request.Role == Role.System)
                throw new ForbiddenException("The System role cannot be assigned through this endpoint.");
            if (user.Role == Role.Admin && user.Status == AccountStatus.Active && request.Role != Role.Admin &&
                await _users.CountActiveByRoleAsync(Role.Admin, transactionCancellationToken) <= 1)
                throw new ConflictException("The last active administrator cannot be demoted.");
            if (user.Role == Role.Teacher && request.Role != Role.Teacher)
                await EnsureTeacherHasNoOwnedResourcesAsync(user.Id, transactionCancellationToken);
            user.SetFullName(request.FullName.Trim());
            user.SetPhone(request.Phone?.Trim() ?? string.Empty);
            user.SetRole(request.Role);
            user.SetModifiedBy(actorId);
            user.RevokeSessions();
            await _unitOfWork.SaveChangesAsync(transactionCancellationToken);
            result = Map(user);
        }

        await ExecuteSerializableAsync(UpdateCore, cancellationToken);
        return result!;
    }

    private void EnsureAdminOrSystem()
    {
        if (!_currentUser.GetRequiredRole().IsAdminOrSystem())
            throw new ForbiddenException("Only administrators can manage users.");
    }

    private static void EnsureManageableTarget(Role actorRole, Role targetRole)
    {
        if (targetRole == Role.System || actorRole == Role.Admin && targetRole == Role.Admin)
            throw new ForbiddenException("Administrators can only manage student and teacher accounts; system accounts are protected.");
    }
    private Task ExecuteSerializableAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken) =>
        _transactionManager is null
            ? action(cancellationToken)
            : _transactionManager.ExecuteSerializableAsync(action, cancellationToken);
    private async Task EnsureTeacherHasNoOwnedResourcesAsync(Guid teacherId, CancellationToken cancellationToken)
    {
        var courseCount = await _courses.CountAsync(
            null,
            null,
            teacherId,
            true,
            cancellationToken);
        var questionBanks = await _questionBanks.ListByTeacherAsync(teacherId, cancellationToken);
        if (courseCount > 0 || questionBanks.Count > 0)
        {
            throw new ConflictException(
                "Transfer the teacher's courses and question banks before changing their role or active status.");
        }
    }
    private static void ValidatePage(int page, int size) { if (page <= 0 || size is < 1 or > 100) throw new BadRequestException("Invalid pagination."); }
    private static string NormalizeEmail(string email) { var value = email?.Trim().ToLowerInvariant(); if (string.IsNullOrWhiteSpace(value) || value.Length > 255 || !MailAddress.TryCreate(value, out var address) || address.Address != value) throw new BadRequestException("Email address is invalid."); return value; }
    private static UserProfileDto Map(User u) => new(u.Id, u.Email, u.FullName, u.Phone, u.Role, u.Status, u.AvatarUrl, u.CreatedAt, u.PasswordHash is not null);
}

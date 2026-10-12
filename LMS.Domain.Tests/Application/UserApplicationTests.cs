using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.persistence;
using LMS.Api.application.features.users;
using LMS.Api.domain.Entities.Users;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;
using Moq;

namespace LMS.Domain.Tests.Application;

public class UserApplicationTests
{
    [Fact]
    public async Task ListUsersAppliesRoleAndStatusFiltersToPageAndCount()
    {
        var actor = CreateAdmin();
        var fixture = CreateFixture(actor, actor, adminCount: 1);
        fixture.Users.Setup(repository => repository.ListAsync(
                "query",
                Role.Teacher,
                AccountStatus.Disabled,
                20,
                10,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<User>());
        fixture.Users.Setup(repository => repository.CountAsync(
                "query",
                Role.Teacher,
                AccountStatus.Disabled,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var result = await fixture.Service.ListAsync(new UserQuery(
            "query",
            Role.Teacher,
            AccountStatus.Disabled,
            Page: 3,
            PageSize: 10));

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        fixture.Users.Verify(repository => repository.ListAsync(
            "query",
            Role.Teacher,
            AccountStatus.Disabled,
            20,
            10,
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Users.Verify(repository => repository.CountAsync(
            "query",
            Role.Teacher,
            AccountStatus.Disabled,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AdminCannotDemoteAnotherAdministrator()
    {
        var actor = CreateAdmin();
        var target = CreateAdmin();
        var fixture = CreateFixture(actor, target, adminCount: 2);

        await Assert.ThrowsAsync<ForbiddenException>(() => fixture.Service.UpdateByAdminAsync(
            target.Id,
            new AdminUpdateUserRequest(target.FullName, target.Phone, Role.Student)));

        Assert.Equal(Role.Admin, target.Role);
        fixture.Users.Verify(repository => repository.CountActiveByRoleAsync(
            It.IsAny<Role>(),
            It.IsAny<CancellationToken>()), Times.Never);
        fixture.UnitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdminCannotEditOwnAdministratorAccount()
    {
        var actor = CreateAdmin();
        var fixture = CreateFixture(actor, actor, adminCount: 1);

        await Assert.ThrowsAsync<ForbiddenException>(() => fixture.Service.UpdateByAdminAsync(
            actor.Id,
            new AdminUpdateUserRequest(actor.FullName, actor.Phone, Role.Student)));

        Assert.Equal(Role.Admin, actor.Role);
        fixture.UnitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdminCannotChangeAnotherAdministratorStatus()
    {
        var actor = CreateAdmin();
        var target = CreateAdmin();
        var fixture = CreateFixture(actor, target, adminCount: 2);

        await Assert.ThrowsAsync<ForbiddenException>(() => fixture.Service.UpdateStatusAsync(
            target.Id,
            new UpdateUserStatusRequest(AccountStatus.Disabled)));

        Assert.Equal(AccountStatus.Active, target.Status);
        fixture.UnitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SystemCanDemoteAdministratorWhenAnotherAdministratorRemains()
    {
        var actor = CreateSystem();
        var target = CreateAdmin();
        var fixture = CreateFixture(actor, target, adminCount: 2);

        var result = await fixture.Service.UpdateByAdminAsync(
            target.Id,
            new AdminUpdateUserRequest(target.FullName, target.Phone, Role.Student));

        Assert.Equal(Role.Student, result.Role);
        fixture.UnitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(2, target.AuthVersion);
    }

    [Fact]
    public async Task SystemCannotDisableLastActiveAdministrator()
    {
        var actor = CreateSystem();
        var target = CreateAdmin();
        var fixture = CreateFixture(actor, target, adminCount: 1);

        await Assert.ThrowsAsync<ConflictException>(() => fixture.Service.UpdateStatusAsync(
            target.Id,
            new UpdateUserStatusRequest(AccountStatus.Disabled)));

        Assert.Equal(AccountStatus.Active, target.Status);
        fixture.UnitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdminCannotCreateAnotherAdministrator()
    {
        var actor = CreateAdmin();
        var fixture = CreateFixture(actor, actor, adminCount: 1);

        await Assert.ThrowsAsync<ForbiddenException>(() => fixture.Service.CreateByAdminAsync(
            new AdminCreateUserRequest(
                "new-admin@example.com",
                "New administrator",
                null,
                "password123",
                Role.Admin)));

        fixture.Users.Verify(repository => repository.Add(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task SystemCanCreateAnAdministrator()
    {
        var actor = CreateSystem();
        var fixture = CreateFixture(actor, actor, adminCount: 1);

        var result = await fixture.Service.CreateByAdminAsync(new AdminCreateUserRequest(
            "new-admin@example.com",
            "New administrator",
            null,
            "password123",
            Role.Admin));

        Assert.Equal(Role.Admin, result.Role);
        fixture.Users.Verify(repository => repository.Add(It.Is<User>(user => user.Role == Role.Admin)), Times.Once);
        fixture.UnitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SystemRoleCannotBeAssignedThroughUserCreation()
    {
        var actor = CreateSystem();
        var fixture = CreateFixture(actor, actor, adminCount: 1);

        await Assert.ThrowsAsync<ForbiddenException>(() => fixture.Service.CreateByAdminAsync(
            new AdminCreateUserRequest(
                "new-system@example.com",
                "New system account",
                null,
                "password123",
                Role.System)));

        fixture.Users.Verify(repository => repository.Add(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task SystemCannotModifySystemAccount()
    {
        var system = CreateSystem();
        var fixture = CreateFixture(system, system, adminCount: 1);

        await Assert.ThrowsAsync<ForbiddenException>(() => fixture.Service.UpdateStatusAsync(
            system.Id,
            new UpdateUserStatusRequest(AccountStatus.Disabled)));

        Assert.Equal(AccountStatus.Active, system.Status);
        fixture.UnitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdminCannotChangeTeacherIntoAdministrator()
    {
        var actor = CreateAdmin();
        var teacher = new User(
            $"{Guid.NewGuid():N}@example.com",
            "Teacher",
            null,
            "hash",
            Role.Teacher,
            AccountStatus.Active);
        var fixture = CreateFixture(actor, teacher, adminCount: 1);

        await Assert.ThrowsAsync<ForbiddenException>(() => fixture.Service.UpdateByAdminAsync(
            teacher.Id,
            new AdminUpdateUserRequest(teacher.FullName, teacher.Phone, Role.Admin)));

        Assert.Equal(Role.Teacher, teacher.Role);
        fixture.UnitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TeacherWithOwnedCourseCannotBeDisabled()
    {
        var actor = CreateAdmin();
        var teacher = new User(
            $"{Guid.NewGuid():N}@example.com",
            "Teacher",
            null,
            "hash",
            Role.Teacher,
            AccountStatus.Active);
        var users = new Mock<IUserManagementRepository>();
        users.Setup(repository => repository.GetByIdAsync(teacher.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(teacher);
        var courses = new Mock<ICourseRepository>();
        courses.Setup(repository => repository.CountAsync(
                null, null, teacher.Id, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.IsAuthenticated).Returns(true);
        currentUser.SetupGet(user => user.UserId).Returns(actor.Id);
        currentUser.SetupGet(user => user.Role).Returns(actor.Role);
        var unitOfWork = new Mock<IUnitOfWork>();
        var service = new UserService(
            users.Object,
            Mock.Of<IPasswordHasher>(),
            unitOfWork.Object,
            currentUser.Object,
            courses.Object,
            Mock.Of<IQuestionBankRepository>());

        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateStatusAsync(
            teacher.Id,
            new UpdateUserStatusRequest(AccountStatus.Disabled)));

        Assert.Equal(AccountStatus.Active, teacher.Status);
        unitOfWork.Verify(item => item.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdminCreateRejectsUndefinedRole()
    {
        var actor = CreateAdmin();
        var fixture = CreateFixture(actor, actor, adminCount: 1);

        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.CreateByAdminAsync(
            new AdminCreateUserRequest(
                "new@example.com",
                "New user",
                null,
                "password123",
                (Role)999)));
    }

    [Fact]
    public async Task AdminCreateRejectsPhoneLongerThanDatabaseColumn()
    {
        var actor = CreateAdmin();
        var fixture = CreateFixture(actor, actor, adminCount: 1);

        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.CreateByAdminAsync(
            new AdminCreateUserRequest(
                "new@example.com",
                "New user",
                new string('1', 21),
                "password123",
                Role.Student)));
    }

    [Fact]
    public async Task AdminCreateRejectsEmailLongerThanDatabaseColumn()
    {
        var actor = CreateAdmin();
        var fixture = CreateFixture(actor, actor, adminCount: 1);

        await Assert.ThrowsAsync<BadRequestException>(() => fixture.Service.CreateByAdminAsync(
            new AdminCreateUserRequest(
                $"{new string('a', 244)}@example.com",
                "New user",
                null,
                "password123",
                Role.Student)));
    }

    [Fact]
    public async Task AuthenticatedUserCanUpdateOwnProfile()
    {
        var actor = CreateAdmin();
        var fixture = CreateFixture(actor, actor, adminCount: 1);

        var result = await fixture.Service.UpdateMyProfileAsync(
            new UpdateMyProfileRequest("  Updated profile  ", " 0901234567 "));

        Assert.Equal("Updated profile", result.FullName);
        Assert.Equal("0901234567", result.Phone);
        Assert.Equal(actor.Id, actor.ModifiedById);
        fixture.UnitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProfileReportsWhetherGoogleAccountIsLinked(bool linked)
    {
        var actor = CreateAdmin();
        var fixture = CreateFixture(actor, actor, adminCount: 1);
        var externalAccount = linked
            ? new ExternalUser(actor.Id, ExternalProvider.Google, "google-user-id")
            : null;
        fixture.Users.Setup(repository => repository.GetExternalLoginAsync(
                actor.Id,
                ExternalProvider.Google,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(externalAccount);

        var result = await fixture.Service.GetProfileAsync(actor.Id);

        Assert.Equal(linked, result.IsGoogleLinked);
        fixture.Users.Verify(repository => repository.GetExternalLoginAsync(
            actor.Id,
            ExternalProvider.Google,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LocalUserCanChangePasswordAndRevokeAllSessions()
    {
        var actor = CreateAdmin();
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(item => item.Verify("current-password", "hash")).Returns(true);
        hasher.Setup(item => item.Verify("new-password", "hash")).Returns(false);
        hasher.Setup(item => item.Hash("new-password")).Returns("new-hash");
        var fixture = CreateFixture(actor, actor, adminCount: 1, hasher.Object);

        await fixture.Service.ChangeMyPasswordAsync(new ChangeMyPasswordRequest(
            "current-password",
            "new-password",
            "new-password"));

        Assert.Equal("new-hash", actor.PasswordHash);
        Assert.Equal(2, actor.AuthVersion);
        Assert.Equal(actor.Id, actor.ModifiedById);
        fixture.UnitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangePasswordRejectsIncorrectCurrentPassword()
    {
        var actor = CreateAdmin();
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(item => item.Verify(It.IsAny<string>(), "hash")).Returns(false);
        var fixture = CreateFixture(actor, actor, adminCount: 1, hasher.Object);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.ChangeMyPasswordAsync(new ChangeMyPasswordRequest(
                "incorrect-password",
                "new-password",
                "new-password")));

        Assert.Equal("hash", actor.PasswordHash);
        Assert.Equal(1, actor.AuthVersion);
        fixture.UnitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OAuthOnlyUserCanCreateLocalPassword()
    {
        var actor = new User(
            $"{Guid.NewGuid():N}@example.com",
            "OAuth user",
            null,
            null,
            Role.Student,
            AccountStatus.Active);
        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(item => item.Hash("new-password")).Returns("new-hash");
        var fixture = CreateFixture(actor, actor, adminCount: 1, hasher.Object);

        await fixture.Service.ChangeMyPasswordAsync(new ChangeMyPasswordRequest(
            null,
            "new-password",
            "new-password"));

        Assert.Equal("new-hash", actor.PasswordHash);
        Assert.Equal(2, actor.AuthVersion);
    }

    [Fact]
    public async Task ChatSearchReturnsMinimalProfileForAnotherActiveUser()
    {
        var actor = CreateAdmin();
        var target = new User(
            "friend@example.com",
            "Chat friend",
            null,
            "hash",
            Role.Student,
            AccountStatus.Active);
        var fixture = CreateFixture(actor, actor, adminCount: 1);
        fixture.Users.Setup(repository => repository.GetByEmailAsync(
                "friend@example.com",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);

        var result = await fixture.Service.FindForChatByEmailAsync(" FRIEND@example.com ");

        Assert.Equal(target.Id, result.Id);
        Assert.Equal(target.Email, result.Email);
        Assert.Equal(target.FullName, result.FullName);
    }

    [Fact]
    public async Task ChatSearchRejectsSearchingForSelf()
    {
        var actor = CreateAdmin();
        var fixture = CreateFixture(actor, actor, adminCount: 1);
        fixture.Users.Setup(repository => repository.GetByEmailAsync(
                actor.Email,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(actor);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.FindForChatByEmailAsync(actor.Email));
    }

    [Fact]
    public async Task ChatNameSearchReturnsLimitedMinimalProfiles()
    {
        var actor = CreateAdmin();
        var first = new User(
            "first@example.com",
            "Nguyen An",
            null,
            "hash",
            Role.Student,
            AccountStatus.Active);
        var second = new User(
            "second@example.com",
            "Nguyen Binh",
            null,
            "hash",
            Role.Teacher,
            AccountStatus.Active);
        var fixture = CreateFixture(actor, actor, adminCount: 1);
        fixture.Users.Setup(repository => repository.SearchActiveForChatByNameAsync(
                "Nguyen",
                actor.Id,
                10,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second]);

        var result = await fixture.Service.SearchForChatByNameAsync("  Nguyen  ");

        Assert.Collection(
            result,
            item => Assert.Equal(first.Email, item.Email),
            item => Assert.Equal(second.Email, item.Email));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    public async Task ChatNameSearchRejectsShortQueries(string name)
    {
        var actor = CreateAdmin();
        var fixture = CreateFixture(actor, actor, adminCount: 1);

        await Assert.ThrowsAsync<BadRequestException>(() =>
            fixture.Service.SearchForChatByNameAsync(name));

        fixture.Users.Verify(repository => repository.SearchActiveForChatByNameAsync(
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Fixture CreateFixture(
        User actor,
        User target,
        int adminCount,
        IPasswordHasher? passwordHasher = null)
    {
        var users = new Mock<IUserManagementRepository>();
        users.Setup(repository => repository.GetByIdAsync(target.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(target);
        users.Setup(repository => repository.CountActiveByRoleAsync(Role.Admin, It.IsAny<CancellationToken>()))
            .ReturnsAsync(adminCount);

        var currentUser = new Mock<ICurrentUser>();
        currentUser.SetupGet(user => user.IsAuthenticated).Returns(true);
        currentUser.SetupGet(user => user.UserId).Returns(actor.Id);
        currentUser.SetupGet(user => user.Role).Returns(actor.Role);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(unit => unit.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var service = new UserService(
            users.Object,
            passwordHasher ?? Mock.Of<IPasswordHasher>(),
            unitOfWork.Object,
            currentUser.Object,
            Mock.Of<ICourseRepository>(),
            Mock.Of<IQuestionBankRepository>());
        return new Fixture(service, users, unitOfWork);
    }

    private static User CreateAdmin() => new(
        $"{Guid.NewGuid():N}@example.com",
        "Administrator",
        null,
        "hash",
        Role.Admin,
        AccountStatus.Active);

    private static User CreateSystem() => new(
        $"{Guid.NewGuid():N}@example.com",
        "System",
        null,
        "hash",
        Role.System,
        AccountStatus.Active);

    private sealed record Fixture(
        UserService Service,
        Mock<IUserManagementRepository> Users,
        Mock<IUnitOfWork> UnitOfWork);
}

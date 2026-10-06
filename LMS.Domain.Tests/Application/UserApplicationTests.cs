using LMS.Api.application.abstractions.auth;
using LMS.Api.application.abstractions.caching;
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
    public async Task LastAdministratorCannotDemoteSelf()
    {
        var actor = CreateAdmin();
        var fixture = CreateFixture(actor, actor, adminCount: 1);

        await Assert.ThrowsAsync<ConflictException>(() => fixture.Service.UpdateByAdminAsync(
            actor.Id,
            new AdminUpdateUserRequest(actor.FullName, actor.Phone, Role.Student)));

        Assert.Equal(Role.Admin, actor.Role);
        fixture.UnitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AdministratorCanDemoteSelfWhenAnotherAdministratorRemains()
    {
        var actor = CreateAdmin();
        var fixture = CreateFixture(actor, actor, adminCount: 2);

        var result = await fixture.Service.UpdateByAdminAsync(
            actor.Id,
            new AdminUpdateUserRequest(actor.FullName, actor.Phone, Role.Student));

        Assert.Equal(Role.Student, result.Role);
        fixture.UnitOfWork.Verify(unitOfWork => unitOfWork.SaveChangesAsync(
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Cache.Verify(cache => cache.IncrementAsync(
            $"auth:version:{actor.Id:N}",
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LastActiveAdministratorCannotDisableSelf()
    {
        var actor = CreateAdmin();
        var fixture = CreateFixture(actor, actor, adminCount: 1);

        await Assert.ThrowsAsync<ConflictException>(() => fixture.Service.UpdateStatusAsync(
            actor.Id,
            new UpdateUserStatusRequest(AccountStatus.Disabled)));

        Assert.Equal(AccountStatus.Active, actor.Status);
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
            Mock.Of<ICache>(),
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

    private static Fixture CreateFixture(User actor, User target, int adminCount)
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

        var cache = new Mock<ICache>();
        cache.Setup(item => item.SetIfNotExistsAsync(
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        cache.Setup(item => item.IncrementAsync(
                It.IsAny<string>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var service = new UserService(
            users.Object,
            Mock.Of<IPasswordHasher>(),
            unitOfWork.Object,
            currentUser.Object,
            cache.Object,
            Mock.Of<ICourseRepository>(),
            Mock.Of<IQuestionBankRepository>());
        return new Fixture(service, users, unitOfWork, cache);
    }

    private static User CreateAdmin() => new(
        $"{Guid.NewGuid():N}@example.com",
        "Administrator",
        null,
        "hash",
        Role.Admin,
        AccountStatus.Active);

    private sealed record Fixture(
        UserService Service,
        Mock<IUserManagementRepository> Users,
        Mock<IUnitOfWork> UnitOfWork,
        Mock<ICache> Cache);
}

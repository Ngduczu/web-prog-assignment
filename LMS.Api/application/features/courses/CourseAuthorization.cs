using LMS.Api.application.abstractions.auth;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Enums.Courses;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;
using LMS.Api.domain.repositories;

namespace LMS.Api.application.features.courses;

public static class CourseAuthorization
{
    public static Guid EnsureCanManage(ICurrentUser currentUser, Course course)
    {
        var actorId = currentUser.GetRequiredUserId();
        var actorRole = currentUser.GetRequiredRole();

        if (actorRole != Role.Admin && (actorRole != Role.Teacher || course.TeacherId != actorId))
        {
            throw new ForbiddenException("Only the course teacher or an administrator can manage this course.");
        }

        return actorId;
    }

    public static async Task EnsureCanViewContentAsync(
        ICurrentUser currentUser,
        Course course,
        IEnrollmentRepository enrollments,
        CancellationToken cancellationToken = default)
    {
        var actorId = currentUser.GetRequiredUserId();
        var actorRole = currentUser.GetRequiredRole();

        if (actorRole == Role.Admin || (actorRole == Role.Teacher && course.TeacherId == actorId))
        {
            return;
        }

        if (actorRole == Role.Student)
        {
            var enrollment = await enrollments.GetByStudentAndCourseAsync(
                actorId,
                course.Id,
                cancellationToken);
            if (enrollment?.Status == EnrollmentStatus.Accepted)
            {
                return;
            }
        }

        throw new ForbiddenException(
            "Only the course teacher, an administrator, or an accepted student can view course content.");
    }

    public static bool IsCourseManager(ICurrentUser currentUser, Course course)
    {
        var actorId = currentUser.GetRequiredUserId();
        var actorRole = currentUser.GetRequiredRole();
        return actorRole == Role.Admin || (actorRole == Role.Teacher && course.TeacherId == actorId);
    }
}

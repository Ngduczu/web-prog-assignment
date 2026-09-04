using LMS.Api.application.abstractions.auth;
using LMS.Api.domain.Entities.Courses;
using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;

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
}

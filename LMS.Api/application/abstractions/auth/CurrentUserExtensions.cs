using LMS.Api.domain.Enums.Users;
using LMS.Api.domain.exceptions;

namespace LMS.Api.application.abstractions.auth;

public static class CurrentUserExtensions
{
    public static Guid GetRequiredUserId(this ICurrentUser currentUser)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is null)
        {
            throw new UnauthorizeException("Authentication is required.");
        }

        return currentUser.UserId.Value;
    }

    public static Role GetRequiredRole(this ICurrentUser currentUser)
    {
        if (!currentUser.IsAuthenticated || currentUser.Role is null)
        {
            throw new UnauthorizeException("Authentication is required.");
        }

        return currentUser.Role.Value;
    }
}

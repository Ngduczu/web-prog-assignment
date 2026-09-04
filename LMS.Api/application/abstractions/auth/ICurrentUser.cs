using LMS.Api.domain.Enums.Users;

namespace LMS.Api.application.abstractions.auth;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    Role? Role { get; }
}
